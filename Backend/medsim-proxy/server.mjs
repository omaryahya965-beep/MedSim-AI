// MedSim AI - secure, scenario-grounded AI proxy (Node 22+, no dependencies).
//
// v0.2 Live AI (lib/live.mjs): two independent personas generate natural replies at runtime; every generated line is
// checked by deterministic grounding rules (lib/grounding.mjs) against authoritative scenario text before it is spoken,
// and all character speech is synthesised at runtime (Realtime API; no pre-recorded dialogue). Spending is capped
// in-app (lib/ledger.mjs). Provider keys come from the environment or a secrets file outside the repo (lib/secrets.mjs).
//
// Legacy classification-only endpoint /v1/converse (kept for compatibility and its tests):
//  * The authoritative scenario (and optional translation) is loaded and validated SERVER-SIDE from disk.
//  * The LLM returns a constrained JSON object (Structured Outputs) and approved text is rendered.
//  * API keys are never logged or returned.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadSecrets, redact } from './lib/secrets.mjs';
import { Ledger } from './lib/ledger.mjs';
import { OpenAIClient } from './lib/openai.mjs';
import { LiveEngine } from './lib/live.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SECRETS = loadSecrets();
const CFG = {
  port: Number(process.env.PORT || 8787),
  host: process.env.MEDSIM_HOST || '127.0.0.1',
  clientToken: SECRETS.clientToken,
  scenarioDir: process.env.MEDSIM_SCENARIO_DIR || path.resolve(HERE, '../../Assets/MedSimAI/Resources/Scenarios'),
  openaiKey: SECRETS.openaiKey,
  chatModel: process.env.MEDSIM_CHAT_MODEL || 'gpt-6-luna',
  sttModel: process.env.MEDSIM_STT_MODEL || 'gpt-transcribe',
  ttsModel: process.env.MEDSIM_TTS_MODEL || 'gpt-realtime-2.1-mini',
  ttsFallbackModel: process.env.MEDSIM_TTS_FALLBACK_MODEL || 'gpt-4o-mini-tts', // /v1/audio/speech shuts down 2027-01-06
  timeoutMs: Number(process.env.MEDSIM_UPSTREAM_TIMEOUT_MS || 20000),
  budgetUsd: Number(process.env.MEDSIM_BUDGET_USD || 2),
  maxBody: 4 * 1024 * 1024,
};
export const ledger = new Ledger({ budgetUsd: CFG.budgetUsd });
export const ai = new OpenAIClient({ apiKey: CFG.openaiKey, ledger, timeoutMs: CFG.timeoutMs, models: { chat: CFG.chatModel, stt: CFG.sttModel, tts: CFG.ttsModel, ttsFallback: CFG.ttsFallbackModel } });

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

// ------------------------------------------------------------------ Live AI (v0.2)
export const live = new LiveEngine({ ai, scenarios });

function wavSeconds(buf) {
  try { if (buf.toString('ascii', 0, 4) !== 'RIFF') return buf.length / 32000; const rate = buf.readUInt32LE(24), ch = buf.readUInt16LE(22), bits = buf.readUInt16LE(34); return (buf.length - 44) / (rate * ch * bits / 8); } catch { return 0; }
}
const STT_PROMPT = {
  en: 'A medical student talking to a patient with chest pain and to a supervising doctor: ECG, troponin, STEMI, cardiology, cath lab, auscultation, SpO2, blood pressure, allergies, penicillin, amlodipine, atorvastatin.',
  ar: 'طالب طب يتحدث باللهجة الفلسطينية مع مريض عنده ألم في الصدر ومع الطبيبة المشرفة: تخطيط القلب، ضغط الدم، حساسية، أدوية، قسطرة، طبيب القلب.',
};

// ------------------------------------------------------------------ HTTP
function readBody(req) {
  return new Promise((res, rej) => { const c = []; let n = 0;
    req.on('data', d => { n += d.length; if (n > CFG.maxBody) { rej(new Error('too large')); req.destroy(); } else c.push(d); });
    req.on('end', () => res(Buffer.concat(c))); req.on('error', rej); });
}
function send(res, status, obj, type = 'application/json') { res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' }); res.end(type === 'application/json' ? JSON.stringify(obj) : obj); }
function errStatus(e) { if (e.code === 'budget_exceeded') return [402, 'budget_exceeded']; if (e.status === 401 || e.status === 403) return [502, 'provider_auth_failed']; if (e.status === 429) return [429, 'provider_rate_limited']; if (e.name === 'TimeoutError' || /timeout/i.test(e.message)) return [504, 'upstream_timeout']; if (e instanceof SyntaxError) return [400, 'bad_json']; return [502, 'upstream_failure']; }

export function createServer() {
  return http.createServer(async (req, res) => {
    const started = Date.now(); const route = req.url.split('?')[0];
    try {
      if (route === '/health') return send(res, 200, { ok: true, scenarios: [...scenarios.keys()], providerConfigured: !!CFG.openaiKey, chatModel: CFG.chatModel, sttModel: CFG.sttModel, ttsModel: CFG.ttsModel, budgetRemainingUsd: Number(ledger.remaining().toFixed(4)) });
      if (!CFG.clientToken || req.headers['x-medsim-client'] !== CFG.clientToken) return send(res, 401, { error: 'unauthorized' });
      const url = new URL(req.url, 'http://x');
      if (req.method === 'GET' && route.startsWith('/v1/audio/')) {
        const a = await live.audioFor(route.slice('/v1/audio/'.length));
        if (!a) return send(res, 404, { error: 'unknown_audio' });
        if (a.status !== 'ready') return send(res, a.error === 'budget_exceeded' ? 402 : 502, { error: a.error || 'tts_unavailable' });
        res.writeHead(200, { 'Content-Type': 'audio/wav', 'Cache-Control': 'no-store', 'X-MedSim-TTS-Engine': a.engine || '', 'X-MedSim-TTS-Fidelity': String(a.fidelity ?? '') });
        return res.end(a.wav);
      }
      if (req.method === 'GET' && route === '/v1/usage') return send(res, 200, ledger.summary());
      const body = await readBody(req);
      if (req.method === 'POST' && route === '/v1/stt') {
        if (live.admit(null)) return send(res, 429, { error: 'rate_limited' });
        const sec = wavSeconds(body);
        if (sec > 30) return send(res, 413, { error: 'audio_too_long' });
        const lang = url.searchParams.get('lang') === 'ar' ? 'ar' : 'en';
        const r = await ai.transcribe(body, { lang, durationSec: sec, prompt: STT_PROMPT[lang] });
        return send(res, 200, { text: r.text, ms: r.ms, model: CFG.sttModel }); // audio is not stored
      }
      // ---- native voice: student audio in, validated streamed speech out (framed: one JSON line, then raw PCM16 24 kHz mono)
      if (req.method === 'POST' && (route === '/v1/voice' || route === '/v1/say-stream')) {
        const isVoice = route === '/v1/voice';
        let opts = {};
        if (isVoice) { try { opts = JSON.parse(Buffer.from(url.searchParams.get('ctx') || 'e30', 'base64url').toString('utf8')); } catch { return send(res, 400, { error: 'bad_ctx' }); } }
        else opts = body.length ? JSON.parse(body.toString('utf8')) : {};
        const sess = live.get(isVoice ? url.searchParams.get('sessionId') : opts.sessionId);
        if (!sess) return send(res, 404, { error: 'unknown_session' });
        const lim = live.admit(sess); if (lim) return send(res, 429, { error: lim });
        if (isVoice && (body.length < 4800 || body.length > 48000 * 30)) return send(res, 413, { error: body.length < 4800 ? 'audio_too_short' : 'audio_too_long' });
        const ac = new AbortController();
        res.on('close', () => { if (!res.writableEnded) ac.abort(); });   // student interrupted → cancel speech upstream
        let headerSent = false;
        const onMeta = m => { if (headerSent) return; headerSent = true; res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Cache-Control': 'no-store', 'X-Accel-Buffering': 'no' }); res.write(JSON.stringify(m) + '\n'); };
        const onAudio = chunk => { if (!res.writableEnded && !ac.signal.aborted) res.write(chunk); };
        try {
          const out = isVoice
            ? await live.voiceTurn(sess, url.searchParams.get('persona') === 'Patient' ? 'Patient' : 'Instructor', body, { language: url.searchParams.get('lang') || undefined, context: opts }, { onMeta, onAudio, signal: ac.signal })
            : await live.sayStream(sess, opts, { onMeta, onAudio, signal: ac.signal });
          if (out) console.log(`  ${route} firstAudio=${out.firstAudioMs}ms total=${out.totalMs}ms fidelity=${out.fidelity == null ? '-' : out.fidelity.toFixed(2)}${out.cancelled ? ' (interrupted)' : ''} ${out.persona || ''} source=${out.source || '-'}${out.understandMs != null ? ' understand=' + out.understandMs + 'ms' : ''}${out.rejected ? ' rejected=' + out.rejected : ''}`);
        } catch (e) {
          if (!headerSent) throw e;                      // normal error response path
          console.error(`[${route}] stream error ${redact(e.message)}`);
        }
        if (!res.writableEnded) res.end();
        return;
      }
      const json = body.length ? JSON.parse(body.toString('utf8')) : {};
      if (req.method === 'POST' && route === '/v1/session/start') { const r = live.start(json); if (r.status === 200) live.prewarm(live.get(r.json.sessionId)); return send(res, r.status, r.json); }
      if (req.method === 'POST' && route === '/v1/session/end') { const r = live.end(json.sessionId); return send(res, r.status, r.json); }
      if (req.method === 'POST' && (route === '/v1/turn' || route === '/v1/say')) {
        const sess = live.get(json.sessionId);
        if (!sess) return send(res, 404, { error: 'unknown_session' });
        const lim = live.admit(sess); if (lim) return send(res, 429, { error: lim });
        const r = route === '/v1/say' ? await live.say(sess, json) : json.persona === 'Patient' ? await live.patientTurn(sess, json) : json.persona === 'Instructor' ? await live.instructorTurn(sess, json) : { status: 400, json: { error: 'bad_persona' } };
        return send(res, r.status, r.json);
      }
      return send(res, 404, { error: 'not_found' });
    } catch (e) {
      // Never log request bodies, audio or keys. Error class and redacted message only.
      console.error(`[${route}] ${redact(e.message)}`);
      const [st, code] = errStatus(e);
      return send(res, st, { error: code });
    } finally { if (route !== '/health') console.log(`${req.method} ${route} ${res.statusCode} ${Date.now() - started}ms`); }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  loadScenarios();
  if (!CFG.clientToken) console.warn('MEDSIM_CLIENT_TOKEN is not set: every API route will refuse requests (401).');
  createServer().listen(CFG.port, CFG.host, () => console.log(`MedSim proxy http://${CFG.host}:${CFG.port} scenarios=[${[...scenarios.keys()]}] provider=${CFG.openaiKey ? 'configured (' + SECRETS.source + ')' : 'NOT configured'} chat=${CFG.chatModel} stt=${CFG.sttModel} tts=${CFG.ttsModel} budget=$${CFG.budgetUsd} spent=$${ledger.spent.toFixed(4)}`));
}
