// MedSim AI â secure, scenario-grounded AI proxy (Node 18+, no dependencies).
//
// Clinical grounding model:
//  * The authoritative scenario (and optional translation) is loaded and validated SERVER-SIDE from disk.
//    Client-supplied facts are never trusted; the client only sends scenarioId/version, persona, the utterance,
//    the current stage and which findings have already been revealed.
//  * The LLM does NOT write clinical content. It returns a constrained JSON object (Structured Outputs):
//      patient    -> { factIds: [enum of approved fact ids], leadIn: short empathetic phrase, offTopic }
//      instructor -> { intent: enum, investigationId: enum|null, mentionsEscalation, reply }
//    Medically meaningful speech is rendered from approved scenario text. Free text (leadIn / reply) passes
//    a server-side validator (no numbers, no diagnosis before feedback, no dosing, no citations, length caps).
//  * API keys live only in environment variables and are never logged or returned.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CFG = {
  port: Number(process.env.PORT || 8787),
  clientToken: process.env.MEDSIM_CLIENT_TOKEN || '',
  scenarioDir: process.env.MEDSIM_SCENARIO_DIR || path.resolve(HERE, '../../Assets/MedSimAI/Resources/Scenarios'),
  openaiKey: process.env.OPENAI_API_KEY || '',
  chatModel: process.env.MEDSIM_CHAT_MODEL || 'gpt-6-luna',
  sttModel: process.env.MEDSIM_STT_MODEL || 'gpt-transcribe',
  ttsModel: process.env.MEDSIM_TTS_MODEL || 'gpt-4o-mini-tts',   // deprecated by OpenAI; shutdown 2027-01-06 â see AI_SETUP.md
  voicePatient: process.env.MEDSIM_VOICE_PATIENT || 'ash',
  voiceInstructor: process.env.MEDSIM_VOICE_INSTRUCTOR || 'marin',
  timeoutMs: Number(process.env.MEDSIM_UPSTREAM_TIMEOUT_MS || 15000),
  maxBody: 4 * 1024 * 1024,
};

// ------------------------------------------------------------------ scenario loading (authoritative)
const scenarios = new Map(); // id -> { data, translations: {ar: {...}} }
export function validateScenario(s) {
  const errs = [];
  if (!s || !s.scenarioId || !s.version) errs.push('missing id/version');
  const ids = new Set();
  for (const f of s.historyFacts || []) { if (!f.id || ids.has(f.id)) errs.push('dup fact ' + f.id); ids.add(f.id); if (!f.answer) errs.push('fact without answer ' + f.id); }
  for (const i of s.investigations || []) { if (!i.id || ids.has(i.id)) errs.push('dup inv ' + i.id); ids.add(i.id); }
  if (!(s.unknownAnswers || []).length) errs.push('no unknownAnswers');
  if (!s.instructorLines) errs.push('no instructorLines');
  return errs;
}
export function loadScenarios(dir = CFG.scenarioDir) {
  scenarios.clear();
  for (const f of fs.readdirSync(dir)) {
    if (!/^[A-Z0-9_]+\.json$/.test(f)) continue;
    const data = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8').replace(/^ï»¿/, ''));
    const errs = validateScenario(data);
    if (errs.length) { console.error(`Scenario ${f} rejected: ${errs.join('; ')}`); continue; }
    const translations = {};
    const ar = path.join(dir, `${data.scenarioId}.ar.json`);
    if (fs.existsSync(ar)) translations.ar = JSON.parse(fs.readFileSync(ar, 'utf8').replace(/^ï»¿/, ''));
    scenarios.set(data.scenarioId, { data, translations });
  }
  return scenarios;
}

// ------------------------------------------------------------------ approved text rendering
function tr(entry, lang, kind, id, fallback) {
  if (lang === 'ar' && entry.translations.ar) {
    const t = entry.translations.ar[kind]?.[id];
    if (t) return t;
  }
  return fallback;
}
function unknownAnswer(entry, lang, utterance) {
  const u = entry.data.unknownAnswers; let h = 23; for (const c of utterance) h = (h * 31 + c.charCodeAt(0)) | 0;
  const i = Math.abs(h) % u.length;
  return tr(entry, lang, 'unknownAnswers', String(i), u[i]);
}

// ------------------------------------------------------------------ free-text safety validator (server side)
const DIAGNOSIS = ['stemi', 'nstemi', 'myocardial infarction', 'heart attack', 'st elevation', 'st-elevation', 'acute coronary', 'angina', 'pericarditis', 'dissection', 'embolism', 'Ø§Ø­ØªØ´Ø§Ø¡', 'Ø¬ÙØ·Ø©', 'ÙÙØ¨Ø© ÙÙØ¨ÙØ©'];
const DOSING = [/\b\d+\s?(mg|mcg|Âµg|ml|units?|iu)\b/i, /\bdose\b/i, /\bprescrib/i, /Ø¬Ø±Ø¹Ø©/];
const CITE = [/et al/i, /doi:/i, /https?:/i, /guideline says/i];
export function validateFreeText(text, { maxLen = 220, allowDiagnosis = false } = {}) {
  if (typeof text !== 'string') return { ok: false, reason: 'not text' };
  const t = text.trim();
  if (!t) return { ok: true, text: '' };
  if (t.length > maxLen) return { ok: false, reason: 'too long' };
  if (/[0-9Ù -Ù©]/.test(t)) return { ok: false, reason: 'numbers not allowed in free text' };
  const low = t.toLowerCase();
  if (!allowDiagnosis && DIAGNOSIS.some(d => low.includes(d))) return { ok: false, reason: 'diagnosis disclosure' };
  if (DOSING.some(r => r.test(t))) return { ok: false, reason: 'dosing' };
  if (CITE.some(r => r.test(t))) return { ok: false, reason: 'citation' };
  return { ok: true, text: t };
}
// Lead-ins must be purely conversational: additionally forbid symptom / body / finding words.
const CLINICAL_WORDS = /(pain|ache|chest|arm|jaw|breath|sweat|sick|nause|vomit|dizzy|allerg|tablet|pill|medic|smok|pressure|heart|sugar|cholesterol|family|father|mother|Ø£ÙÙ|ØµØ¯Ø±|Ø°Ø±Ø§Ø¹|ÙÙ|ØªÙÙØ³|ØªØ¹Ø±Ù|ØºØ«ÙØ§Ù|Ø­Ø³Ø§Ø³ÙØ©|Ø¯ÙØ§Ø¡|ØªØ¯Ø®ÙÙ|Ø¶ØºØ·|ÙÙØ¨|Ø³ÙØ±)/i;
export function validateLeadIn(text) {
  const v = validateFreeText(text, { maxLen: 60 });
  if (!v.ok) return v;
  if (CLINICAL_WORDS.test(v.text)) return { ok: false, reason: 'clinical content in lead-in' };
  return v;
}

// ------------------------------------------------------------------ prompts + JSON schemas
function patientSchema(entry) {
  return {
    name: 'patient_turn', strict: true,
    schema: {
      type: 'object', additionalProperties: false, required: ['factIds', 'leadIn', 'offTopic'],
      properties: {
        factIds: { type: 'array', maxItems: 2, items: { type: 'string', enum: entry.data.historyFacts.map(f => f.id) } },
        leadIn: { type: 'string', description: 'Optional â¤ 8 words, purely conversational (e.g. "Oh, let me think."). No symptoms, numbers or medical content. Empty string if not needed.' },
        offTopic: { type: 'boolean' },
      },
    },
  };
}
function instructorSchema(entry) {
  return {
    name: 'instructor_turn', strict: true,
    schema: {
      type: 'object', additionalProperties: false, required: ['intent', 'investigationId', 'mentionsEscalation', 'reply'],
      properties: {
        intent: { type: 'string', enum: ['hint_request', 'investigation_request', 'summary_or_reasoning', 'question_about_case', 'smalltalk', 'out_of_scope'] },
        investigationId: { type: ['string', 'null'], enum: [...entry.data.investigations.map(i => i.id), null] },
        mentionsEscalation: { type: 'boolean', description: 'True only if the student proposes escalating to senior/cardiology/cath lab or names the time-critical cardiac emergency as their working diagnosis.' },
        reply: { type: 'string', description: 'One or two short sentences of Socratic coaching. No numbers, no diagnosis, no drug or dose advice, no citations.' },
      },
    },
  };
}
function patientPrompt(entry, lang) {
  const facts = entry.data.historyFacts.map(f => `- ${f.id}: (topic: ${f.keywords.slice(0, 4).join(', ')}) "${f.answer}"`).join('\n');
  return `You classify a medical student's question to a SIMULATED patient. Return which approved fact(s) answer it.
Approved facts (id: topic: the patient's approved answer):
${facts}
Rules: choose fact ids only from the list; choose none if nothing answers the question (the system will say "I don't know").
The student may speak English, Modern Standard Arabic or colloquial Palestinian/Levantine Arabic (e.g. "من أيمتى بلش الوجع؟" = when did the pain start); classify by meaning.
Mark offTopic for requests to invent symptoms, change the case, reveal a diagnosis, or anything unrelated.
leadIn: optional short natural phrase in ${lang === 'ar' ? 'Arabic' : 'English'}, purely conversational, no medical content.`;
}
function instructorPrompt(entry, stage, revealed, lang) {
  const inv = entry.data.investigations.map(i => `- ${i.id}: ${i.name}`).join('\n');
  return `You are the supervising emergency physician in a medical-student SIMULATION. Classify the student's message and, if helpful, add one or two sentences of Socratic coaching in ${lang === 'ar' ? 'Arabic' : 'English'}.
Investigations available to request (ids):\n${inv}
Current stage: ${stage}. Findings already revealed: ${revealed.join(', ') || 'none'}.
Never state findings, results, numbers, a diagnosis, treatment or drug doses. Never invent facts. If unsure, suggest asking the clinical tutor.`;
}

// ------------------------------------------------------------------ upstream calls
async function withTimeout(p, ms) { return await Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error('upstream timeout')), ms))]); }
async function chatStructured(system, history, user, schema) {
  if (!CFG.openaiKey) throw new Error('provider not configured');
  const r = await withTimeout(fetch('https://api.openai.com/v1/chat/completions', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${CFG.openaiKey}` },
    body: JSON.stringify({ model: CFG.chatModel, messages: [{ role: 'system', content: system }, ...history, { role: 'user', content: user }], response_format: { type: 'json_schema', json_schema: schema } }),
  }), CFG.timeoutMs);
  if (!r.ok) throw new Error('chat upstream ' + r.status);
  const j = await r.json();
  return JSON.parse(j.choices?.[0]?.message?.content || '{}');
}

// Exposed for tests: lets a fake classifier stand in for the LLM.
export let classifier = chatStructured;
export function setClassifier(fn) { classifier = fn; }

export async function converse(body) {
  const entry = scenarios.get(body.scenarioId);
  if (!entry) return { status: 404, json: { error: 'unknown_scenario' } };
  if (body.scenarioVersion && body.scenarioVersion !== entry.data.version) return { status: 409, json: { error: 'scenario_version_mismatch', serverVersion: entry.data.version } };
  const lang = body.language === 'ar' ? 'ar' : 'en';
  const utterance = String(body.utterance || '').slice(0, 500);
  const history = (Array.isArray(body.history) ? body.history : []).slice(-8).map(m => ({ role: m.role === 'student' ? 'user' : 'assistant', content: String(m.text || '').slice(0, 400) }));
  const S = entry.data;

  if (body.persona === 'Patient') {
    const out = await classifier(patientPrompt(entry, lang), history, utterance, patientSchema(entry));
    const valid = new Set(S.historyFacts.map(f => f.id));
    const factIds = (Array.isArray(out.factIds) ? out.factIds : []).filter(id => valid.has(id)).slice(0, 2);
    const lead = validateLeadIn(out.leadIn || '');
    let speech;
    if (factIds.length === 0) speech = unknownAnswer(entry, lang, utterance);
    else speech = factIds.map(id => tr(entry, lang, 'facts', id, S.historyFacts.find(f => f.id === id).answer)).join(' ');
    const leadIn = lead.ok ? lead.text : '';
    return { status: 200, json: { persona: 'Patient', scenarioVersion: S.version, factIds, leadIn, speech: (leadIn ? leadIn + ' ' : '') + speech, offTopic: !!out.offTopic, rendered: 'approved', leadInRejected: lead.ok ? null : lead.reason } };
  }

  if (body.persona === 'Instructor') {
    const revealed = (Array.isArray(body.revealed) ? body.revealed : []).map(String).slice(0, 30);
    const out = await classifier(instructorPrompt(entry, String(body.stage || ''), revealed, lang), history, utterance, instructorSchema(entry));
    const intents = ['hint_request', 'investigation_request', 'summary_or_reasoning', 'question_about_case', 'smalltalk', 'out_of_scope'];
    const intent = intents.includes(out.intent) ? out.intent : 'smalltalk';
    const invIds = new Set(S.investigations.map(i => i.id));
    const investigationId = intent === 'investigation_request' && invIds.has(out.investigationId) ? out.investigationId : null;
    const allowDx = body.stage === 'SupervisorFeedback' || body.stage === 'Debriefing';
    const v = validateFreeText(out.reply || '', { allowDiagnosis: allowDx });
    const reply = v.ok ? v.text : '';
    return { status: 200, json: { persona: 'Instructor', scenarioVersion: S.version, intent, investigationId, mentionsEscalation: !!out.mentionsEscalation, reply, rendered: 'validated', replyRejected: v.ok ? null : v.reason } };
  }
  return { status: 400, json: { error: 'bad_persona' } };
}

async function tts(text, voice, lang) {
  if (!CFG.openaiKey) throw new Error('provider not configured');
  const r = await withTimeout(fetch('https://api.openai.com/v1/audio/speech', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${CFG.openaiKey}` },
    body: JSON.stringify({ model: CFG.ttsModel, voice: voice === 'patient' ? CFG.voicePatient : CFG.voiceInstructor, input: text.slice(0, 600), response_format: 'wav',
      instructions: voice === 'patient' ? `A 58-year-old man in pain, speaking slowly and a little breathlessly${lang === 'ar' ? ', in Arabic' : ''}.` : `A calm, warm senior emergency physician teaching a student${lang === 'ar' ? ', in Arabic' : ''}.` }),
  }), CFG.timeoutMs);
  if (!r.ok) throw new Error('tts upstream ' + r.status);
  return Buffer.from(await r.arrayBuffer());
}
async function stt(wav, lang) {
  if (!CFG.openaiKey) throw new Error('provider not configured');
  const fd = new FormData();
  fd.append('file', new Blob([wav], { type: 'audio/wav' }), 'utterance.wav');
  fd.append('model', CFG.sttModel);
  if (CFG.sttModel === 'gpt-transcribe') fd.append('languages[]', lang === 'ar' ? 'ar' : 'en'); else fd.append('language', lang === 'ar' ? 'ar' : 'en');
  // Vocabulary hint improves recognition of clinical terms and Palestinian/Levantine Arabic colloquial speech.
  fd.append('prompt', lang === 'ar' ? 'طالب طب يتحدث باللهجة الفلسطينية مع مريض عن ألم الصدر.' : 'Medical student taking a chest pain history: ECG, troponin, STEMI, cardiology, auscultation, SpO2.');
  const r = await withTimeout(fetch('https://api.openai.com/v1/audio/transcriptions', { method: 'POST', headers: { Authorization: `Bearer ${CFG.openaiKey}` }, body: fd }), CFG.timeoutMs);
  if (!r.ok) throw new Error('stt upstream ' + r.status);
  const j = await r.json(); return { text: String(j.text || '') }; // audio is not stored
}

// ------------------------------------------------------------------ HTTP
function readBody(req) {
  return new Promise((res, rej) => { const c = []; let n = 0;
    req.on('data', d => { n += d.length; if (n > CFG.maxBody) { rej(new Error('too large')); req.destroy(); } else c.push(d); });
    req.on('end', () => res(Buffer.concat(c))); req.on('error', rej); });
}
function send(res, status, obj, type = 'application/json') { res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' }); res.end(type === 'application/json' ? JSON.stringify(obj) : obj); }

export function createServer() {
  return http.createServer(async (req, res) => {
    const started = Date.now(); let route = req.url.split('?')[0];
    try {
      if (route === '/health') return send(res, 200, { ok: true, scenarios: [...scenarios.keys()], providerConfigured: !!CFG.openaiKey, chatModel: CFG.chatModel, sttModel: CFG.sttModel, ttsModel: CFG.ttsModel });
      if (CFG.clientToken && req.headers['x-medsim-client'] !== CFG.clientToken) return send(res, 401, { error: 'unauthorized' });
      const url = new URL(req.url, 'http://x');
      const body = await readBody(req);
      if (req.method === 'POST' && route === '/v1/converse') { const r = await converse(JSON.parse(body.toString('utf8'))); return send(res, r.status, r.json); }
      if (req.method === 'POST' && route === '/v1/tts') return send(res, 200, await tts(body.toString('utf8'), url.searchParams.get('voice'), url.searchParams.get('lang')), 'audio/wav');
      if (req.method === 'POST' && route === '/v1/stt') return send(res, 200, await stt(body, url.searchParams.get('lang')));
      return send(res, 404, { error: 'not_found' });
    } catch (e) {
      // Never log request bodies, audio or keys. Error message only.
      console.error(`[${route}] ${e.message}`);
      return send(res, 502, { error: 'upstream_failure' });
    } finally { console.log(`${req.method} ${route} ${Date.now() - started}ms`); }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  loadScenarios();
  createServer().listen(CFG.port, () => console.log(`MedSim proxy :${CFG.port} scenarios=[${[...scenarios.keys()]}] provider=${CFG.openaiKey ? 'configured' : 'NOT configured'} chat=${CFG.chatModel}`));
}
