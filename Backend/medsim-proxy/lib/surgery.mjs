// Surgical module for the MedSim proxy (orthopaedic training scenario, e.g. OR_TKA_TRAINING_001).
// Reuses the SAME secure backend pieces as the clinical module: OpenAIClient (budget ledger, retries), Realtime
// understanding (native audio in, no separate STT), Realtime voice actor (streamed speech), deterministic grounding and
// the language rules. It is fully independent at the scenario level: separate scenario files, separate sessions, two
// surgical personas (surgeon, assistant) and NO access to the chest-pain scenario, personas or history.
//
// Authority split (same principle as the clinical module):
//  * Unity's deterministic C# core (SurgerySessionCore) decides correctness, objectives, hints, hand-overs, scores.
//  * The AI only (a) understands what the student said and proposes a catalogue intent, and (b) phrases approved text.
//  * Every generated line passes grounding against the approved surgical corpus, or approved text is spoken instead.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { validateGrounded, norm } from './grounding.mjs';
import { scriptLang, detectLanguageCommand, resolveReplyLang } from './language.mjs';
import { speechFidelity, RealtimeUnderstanding } from './openai.mjs';
import { cleanReply, echoWords, isNoSpeech } from './live.mjs';

export const SURGERY_VOICES = {
  surgeon: process.env.MEDSIM_VOICE_SURGEON || 'marin',
  assistant: process.env.MEDSIM_VOICE_ASSISTANT || 'cedar',
};
const STYLE = {
  surgeon: 'a calm, warm physician and clinical educator in her forties supervising a student in the operating room: clear, unhurried, quietly confident; slightly muffled as if speaking through a surgical mask',
  surgeonSafety: 'a calm orthopaedic surgeon who needs the student to stop right now: firm, focused, supportive, slightly quicker',
  surgeonDebrief: 'a warm, encouraging surgical teacher giving personal feedback after a training session: measured and specific',
  assistant: 'a friendly, practical scrub assistant in his thirties: brief, helpful, relaxed; speaking through a surgical mask',
};
export const CHARACTERS = ['surgeon', 'assistant'];

// Must match MedSimAI.Surgery.AI.SurgeryActionCatalog (C#). Unity re-validates everything; this only constrains the model.
export const ACTIONS = {
  assistant: ['none', 'come_to_student', 'stand_beside_student', 'go_to_instrument_table', 'hand_instrument', 'locate_instrument', 'return_to_position', 'wait', 'look_at_student', 'other_physical'],
  surgeon: ['none', 'come_to_student', 'step_back', 'return_to_observation', 'look_at_student', 'look_at_site', 'explain_objective', 'wait', 'other_physical'],
};
const INSTRUMENT_ACTIONS = new Set(['hand_instrument', 'locate_instrument']);

// Surgical clinical terms that must be justified by the approved corpus (in addition to the shared CLINICAL_LEXICON).
export const SURGICAL_LEXICON = ['incision', 'incise', 'cut', 'resect', 'bone', 'femur', 'femoral', 'tibia', 'tibial', 'patella', 'kneecap', 'knee', 'ligament', 'tendon', 'cartilage', 'capsule',
  'implant', 'component', 'prosthe', 'cement', 'insert', 'tray', 'trial', 'guide', 'saw', 'drill', 'scalpel', 'retractor', 'mallet', 'impactor', 'suture', 'stapl', 'wound', 'closure',
  'artery', 'nerve', 'vein', 'bleed', 'blood', 'infection', 'tourniquet', 'anaesthe', 'anesthe', 'sterile', 'millimet', 'degree', 'alignment', 'robot',
  'شق', 'قطع', 'عظم', 'فخذ', 'ساق', 'رضفه', 'ركبه', 'رباط', 'وتر', 'غضروف', 'محفظه', 'مفصل', 'اسمنت', 'فاصل', 'صينيه', 'دليل', 'منشار', 'مثقب', 'مشرط', 'مبعد', 'مطرقه', 'دافع',
  'خياطه', 'تقطيب', 'دباسه', 'جرح', 'شريان', 'عصب', 'وريد', 'نزيف', 'دم', 'التهاب', 'تخدير', 'معقم', 'مليمتر', 'درجه', 'اصطفاف', 'روبوت'];
const SURGICAL_GLOSSARY = [['incision', 'incise', 'شق'], ['cut', 'resect', 'قطع'], ['bone', 'عظم'], ['femur', 'femoral', 'thigh bone', 'فخذ'], ['tibia', 'tibial', 'shin bone', 'ساق'],
  ['knee', 'ركبه'], ['joint', 'مفصل'], ['capsule', 'محفظه'], ['implant', 'component', 'مفصل', 'مكون'], ['insert', 'spacer', 'فاصل'], ['tray', 'صينيه'], ['trial', 'تجريبي'],
  ['guide', 'دليل'], ['saw', 'منشار'], ['drill', 'مثقب'], ['scalpel', 'مشرط'], ['retractor', 'مبعد'], ['mallet', 'مطرقه'], ['impactor', 'دافع'], ['suture', 'needle', 'خياطه', 'تقطيب', 'ابره'],
  ['stapl', 'دباسه'], ['wound', 'جرح'], ['sterile', 'معقم'], ['alignment', 'اصطفاف'], ['millimet', 'مليمتر'], ['degree', 'درجه']];
const META_BLOCK = [/\bai\b/i, /language model/i, /\bprompt\b/i, /\bjson\b/i, /\bfunction\b/i, /ذكاء اصطناعي/];

const n2 = s => norm(s).replace(/ه\b/g, 'ه');
function termIn(term, text) { const t = n2(term); return /^[a-z]{1,3}$/.test(t) ? new RegExp(`(^|[^a-z])${t}([^a-z]|$)`).test(text) : text.includes(t); }
function expand(allowedNorm) {
  const add = []; for (const g of SURGICAL_GLOSSARY) if (g.some(t => termIn(t, allowedNorm))) add.push(...g.map(n2));
  return add.length ? allowedNorm + ' \n ' + add.join(' \n ') : allowedNorm;
}

/** Shared grounding (numbers, clinical lexicon, dosing, citations, diagnosis) + surgical lexicon + meta block. */
export function checkSurgicalGrounding(reply, { corpus = [], studentText = '', maxWords = 60, allowNumbers = true } = {}) {
  const base = validateGrounded(reply, { corpus, studentText, maxWords, allowNumbers, allowMeta: true, allowDiagnosis: false });
  if (!base.ok) return base;
  for (const re of META_BLOCK) if (re.test(reply)) return { ok: false, reason: 'meta_reference' };
  const allowed = expand(n2([...corpus, studentText].join(' \n ')));
  const rn = n2(reply);
  for (const term of SURGICAL_LEXICON) if (termIn(term, rn) && !termIn(term, allowed)) return { ok: false, reason: 'ungrounded_surgical_term', detail: term };
  return { ok: true };
}

// ------------------------------------------------------------------ scenario loading (authoritative, server-side)
export function validateSurgicalScenario(s) {
  const e = [];
  if (!s || !s.scenarioId || !s.version) e.push('missing id/version');
  if (s?.kind !== 'surgical') e.push('kind must be surgical');
  const ids = new Set();
  for (const o of s?.objectives || []) { if (!o.id || ids.has(o.id)) e.push('dup objective ' + o.id); ids.add(o.id); if (!o.title?.en || !o.title?.ar || !o.explanation?.en || !o.explanation?.ar) e.push('objective needs en+ar ' + o.id); }
  for (const o of s?.objectives || []) for (const p of o.prerequisites || []) if (!ids.has(p)) e.push(`unknown prerequisite ${p} in ${o.id}`);
  for (const id of CHARACTERS) if (!(s?.personas || []).some(p => p.id === id)) e.push('missing persona ' + id);
  if (!(s?.limitations || []).length) e.push('no limitations');
  if (s?.review?.status === 'APPROVED' && !s?.review?.reviewer) e.push('APPROVED requires reviewer');
  return e;
}

export function loadSurgicalScenarios(dir) {
  const map = new Map();
  if (!dir || !fs.existsSync(dir)) return map;
  for (const f of fs.readdirSync(dir)) {
    if (!/^[A-Z0-9_]+\.json$/.test(f)) continue;
    const data = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8').replace(/^﻿/, ''));
    const errs = validateSurgicalScenario(data);
    if (errs.length) { console.error(`Surgical scenario ${f} rejected: ${errs.join('; ')}`); continue; }
    map.set(data.scenarioId, { data });
  }
  return map;
}

const L = (t, lang) => (lang === 'ar' ? t?.ar : t?.en) || t?.en || '';
const both = t => [t?.en || '', t?.ar || ''];

/** Every approved surgical text (both languages): the outer bound for anything a character may say. */
export function scenarioCorpus(S) {
  return [S.title, S.summary, S.welcome, ...(S.limitations || []), ...(S.objectives || []).flatMap(o => [o.title, o.explanation, ...(o.hints || [])]),
    ...(S.instruments || []).flatMap(i => [i.name, i.purpose]), ...(S.targets || []).map(t => t.name), ...(S.implants || []).map(i => i.name),
    ...(S.supervisorRules || []).map(r => r.line), ...(S.safetyObservations || []).map(x => x.text), ...(S.assessment || []).map(a => a.label)]
    .flatMap(both).concat((S.personas || []).flatMap(p => [p.summary, ...both(p.displayName)])).filter(Boolean);
}

/** Context sent by Unity is ids only; anything unknown is dropped. */
export function sanitizeSurgeryContext(S, c = {}) {
  const obj = new Set((S.objectives || []).map(o => o.id)), tools = new Set((S.instruments || []).map(i => i.id));
  return {
    objectiveId: obj.has(c.objectiveId) ? c.objectiveId : '',
    completed: (Array.isArray(c.completed) ? c.completed : []).filter(x => obj.has(x)).slice(0, 20),
    holding: tools.has(c.holding) ? c.holding : '',
    recent: (Array.isArray(c.recent) ? c.recent : []).filter(x => typeof x === 'string' && /^[a-z_.:]+$/.test(x)).slice(-8),
  };
}

const LANG_ACK = { surgeon: { ar: 'تمام، بنكمّل بالعربي.', en: "Of course, let's continue in English." }, assistant: { ar: 'أكيد، بالعربي.', en: 'Sure, English it is.' } };

function langRule(characterId, { lock, known }) {
  const ar = characterId === 'surgeon'
    ? 'clear, warm spoken Palestinian Arabic as a female doctor and clinical educator talks to a student (simple Levantine colloquial, short sentences; instrument names may stay in English). Use feminine forms for yourself.'
    : 'friendly, brief spoken Palestinian Arabic as a male scrub assistant talks to a student. Use masculine forms for yourself.';
  if (lock === 'ar') return `Reply in ${ar} The student chose Arabic: ALWAYS reply in Arabic.`;
  if (lock === 'en') return 'Reply in natural spoken English. The student chose English: always reply in English.';
  if (known === 'ar') return `Reply in ${ar}`;
  if (known === 'en') return 'Reply in natural spoken English.';
  return `Reply in the SAME language the student used: Arabic (any dialect or mixed with English terms) → ${ar} English → natural spoken English.`;
}

export class SurgeryEngine {
  constructor({ ai, scenarios, limits = {} }) {
    this.ai = ai; this.scenarios = scenarios; this.sessions = new Map();
    this.limits = { maxSessions: 20, maxTurnsPerSession: 300, maxUtterance: 500, sessionTtlMs: 2 * 3600e3, ...limits };
  }
  start({ scenarioId, scenarioVersion, language }) {
    const entry = this.scenarios.get(scenarioId);
    if (!entry) return { status: 404, json: { error: 'unknown_surgical_scenario' } };
    if (scenarioVersion && scenarioVersion !== entry.data.version) return { status: 409, json: { error: 'scenario_version_mismatch', serverVersion: entry.data.version } };
    this.gc();
    if (this.sessions.size >= this.limits.maxSessions) return { status: 429, json: { error: 'too_many_sessions' } };
    const id = crypto.randomUUID();
    this.sessions.set(id, { id, scenarioId, entry, lang: language === 'ar' ? 'ar' : 'en', created: Date.now(), turns: 0, history: { surgeon: [], assistant: [] }, actionLog: [], recentLines: [], events: [] });
    return { status: 200, json: { sessionId: id, scenarioId, scenarioVersion: entry.data.version, voices: SURGERY_VOICES, reviewStatus: entry.data.review?.status || 'DRAFT' } };
  }
  end(sessionId) { this.sessions.delete(sessionId); for (const k of [...(this.rtu?.keys() || [])]) if (k.startsWith(sessionId + ':')) { this.rtu.get(k).close(); this.rtu.delete(k); } return { status: 200, json: { ok: true } }; }
  get(id) { return this.sessions.get(id); }
  gc() { const now = Date.now(); for (const [k, s] of this.sessions) if (now - s.created > this.limits.sessionTtlMs) this.end(k); }
  admit(sess) { if (sess && ++sess.turns > this.limits.maxTurnsPerSession) return 'session_turn_limit'; return null; }

  /** Structured gameplay context from Unity (ids only) — what really happened; the AI must never contradict it. */
  event(sess, body) {
    const ctx = sanitizeSurgeryContext(sess.entry.data, body.context || {});
    sess.ctx = ctx; return { status: 200, json: { ok: true, context: ctx } };
  }
  actionResult(sess, body) {
    const status = ['completed', 'started', 'denied', 'needs_clarification', 'unavailable', 'interrupted', 'failed', 'handoff_offered', 'handoff_completed', 'handoff_not_taken'].includes(body.status) ? body.status : 'failed';
    sess.actionLog.push({ characterId: CHARACTERS.includes(body.characterId) ? body.characterId : 'assistant', action: String(body.action || '').slice(0, 40), instrumentId: String(body.instrumentId || '').slice(0, 40), status, t: Date.now() });
    if (sess.actionLog.length > 12) sess.actionLog.shift();
    return { status: 200, json: { ok: true } };
  }
  noteLine(sess, text) { sess.recentLines.push({ t: Date.now(), words: echoWords(text) }); if (sess.recentLines.length > 12) sess.recentLines.shift(); }
  isEcho(sess, heard) {
    const w = echoWords(heard); if (w.length < 4) return false;
    for (const l of sess.recentLines) { if (Date.now() - l.t > 60000) continue; const bag = new Set(l.words); if (w.filter(x => bag.has(x)).length / w.length >= 0.6) return true; }
    return false;
  }

  turnCorpus(sess, ctx) {
    const S = sess.entry.data; const cur = (S.objectives || []).find(o => o.id === ctx.objectiveId);
    return [...scenarioCorpus(S).filter(Boolean)].concat(cur ? [...both(cur.title), ...both(cur.explanation)] : []);
  }

  systemPrompt(sess, characterId, ctx, rule) {
    const S = sess.entry.data; const P = (S.personas || []).find(p => p.id === characterId) || {};
    const cur = (S.objectives || []).find(o => o.id === ctx.objectiveId);
    const done = (S.objectives || []).filter(o => ctx.completed.includes(o.id)).map(o => o.title.en);
    const sup = (S.personas || []).find(p => p.id === 'surgeon') || {};
    const other = characterId === 'surgeon' ? 'the scrub assistant Sami (instrument table)' : 'the supervising doctor ' + L(sup.displayName, 'en');
    const acts = (sess.actionLog || []).slice(-4).map(a => `- ${a.characterId}: ${a.action}${a.instrumentId ? ' (' + a.instrumentId + ')' : ''} — ${a.status}`).join('\n');
    return `You are ${L(P.displayName, 'en')} in a SIMULATED operating room for medical-student training (draft educational simulation, not clinically validated). Stay in character.
Who you are: ${P.summary}
How you speak: ${P.speechStyle}
Boundaries: ${P.boundaries}
The other character is ${other}.
Training scenario: ${S.title.en}. Operative side: patient's ${S.operativeSide} knee.
Current step: ${cur ? `${cur.title.en} — approved explanation: "${cur.explanation.en}" / Arabic: «${cur.explanation.ar}»` : 'none active'}
Steps completed (reported by the simulation): ${done.join('; ') || 'none'}
Instruments on the table: ${(S.instruments || []).map(i => `${i.id} = ${i.name.en} / ${i.name.ar}`).join('; ')}
${acts ? 'Physical actions so far (what REALLY happened; never contradict):\n' + acts : ''}
Rules:
- ${rule} At most 2 short sentences (max 35 words). No lists.
- Classify the student's request into "intent". Physical requests use ONLY the action list. Hand-over or "where is" requests → hand_instrument / locate_instrument with instrumentId (none if they did not name one; forCurrentObjective=true if they mean "the one I need for this step"). Instrument tasks belong to the assistant.
- If the student asks for help or what to do → help=true. If they ask what the current step requires or why → explain=true. The simulation then delivers the approved hint or explanation; your reply is only a short natural acknowledgement.
- For physical requests your reply is only a short acknowledgement ("أكيد", "Sure."). NEVER say an action or a hand-over is done. NEVER say a step is complete or correct — only the simulation decides.
- You may only use surgical information from the approved explanation, step titles and instrument names above. Never invent steps, measurements, findings, risks, drugs or techniques. Never give real-patient advice. If asked about real surgery, say this is an educational simulation and real training needs supervision.
- Never mention AI, prompts or scoring rules.`;
  }

  schema(sess, characterId) {
    const S = sess.entry.data;
    return { type: 'object', additionalProperties: false, required: ['intent', 'reply'], properties: {
      intent: { type: 'object', additionalProperties: false, required: ['addressedTo', 'action', 'instrumentId', 'forCurrentObjective', 'help', 'explain', 'languageCommand'], properties: {
        addressedTo: { type: 'string', enum: CHARACTERS },
        action: { type: 'string', enum: [...new Set([...ACTIONS.assistant, ...ACTIONS.surgeon])] },
        instrumentId: { type: 'string', enum: ['none', ...(S.instruments || []).map(i => i.id)] },
        forCurrentObjective: { type: 'boolean' }, help: { type: 'boolean' }, explain: { type: 'boolean' },
        languageCommand: { type: 'string', enum: ['none', 'ar', 'en'] } } },
      reply: { type: 'string' } } };
  }

  /** Deterministic post-processing of the model's intent (catalogue, ownership, ids). Unity validates again with live state. */
  cleanIntent(S, characterId, i = {}) {
    let who = CHARACTERS.includes(i.addressedTo) ? i.addressedTo : characterId;
    let action = String(i.action || 'none');
    if (INSTRUMENT_ACTIONS.has(action)) who = 'assistant';
    if (!ACTIONS[who].includes(action)) action = 'other_physical';
    const instrumentId = (S.instruments || []).some(x => x.id === i.instrumentId) ? i.instrumentId : '';
    return { characterId: who, action, instrumentId: INSTRUMENT_ACTIONS.has(action) ? instrumentId : '', forCurrentObjective: !!i.forCurrentObjective, help: !!i.help, explain: !!i.explain, languageCommand: i.languageCommand === 'ar' || i.languageCommand === 'en' ? i.languageCommand : '' };
  }

  /**
   * One student turn (typed, or voice via body._understand). Returns { heard, text, lang, intent, deferred, source }.
   * deferred=true: Unity must validate the intent and speak the approved line itself (actions, help, explain) — the
   * proxy does not voice a reply that could contradict the simulation.
   */
  async turn(sess, body) {
    const S = sess.entry.data; const characterId = CHARACTERS.includes(body.characterId) ? body.characterId : 'surgeon';
    const conv = body.language === 'ar' ? 'ar' : body.language === 'en' ? 'en' : sess.lang;
    const lock = body.langLock === 'ar' || body.langLock === 'en' ? body.langLock : null;
    const ctx = sanitizeSurgeryContext(S, body.context || sess.ctx || {});
    let utterance = String(body.utterance || '').slice(0, this.limits.maxUtterance);
    const ack = cmd => { const t = LANG_ACK[characterId][cmd]; sess.lang = cmd; return { status: 200, json: { characterId, heard: utterance, text: t, lang: cmd, intent: { ...this.cleanIntent(S, characterId, {}), languageCommand: cmd }, deferred: false, source: 'language-command' } }; };
    if (!body._understand) { const cmd = detectLanguageCommand(utterance); if (cmd) return ack(cmd); }
    let lang = body._understand ? (lock || conv) : resolveReplyLang({ lock, heard: utterance, conversation: conv });
    const system = this.systemPrompt(sess, characterId, ctx, langRule(characterId, body._understand ? { lock } : { lock, known: lang }));
    const schema = this.schema(sess, characterId);
    let r = body._understand ? await body._understand({ system, schema })
      : await this.ai.json({ system, messages: [...sess.history[characterId].slice(-10), { role: 'user', content: utterance }], schema, name: 'surgery_turn', maxOutputTokens: 300, tag: 'surgery-' + characterId });
    if (body._understand) {
      utterance = String(r.data.heard || '').slice(0, this.limits.maxUtterance);
      const cmd = detectLanguageCommand(utterance); if (cmd) return ack(cmd);
      lang = resolveReplyLang({ lock, heard: utterance, conversation: conv, spoken: r.data.spokenLanguage });
    }
    const intent = this.cleanIntent(S, characterId, r.data.intent);
    if (intent.languageCommand) { sess.lang = intent.languageCommand; lang = intent.languageCommand; }
    const deferred = intent.action !== 'none' || intent.help || intent.explain;
    let reply = cleanReply(r.data.reply), source = 'ai';
    const v = checkSurgicalGrounding(reply, { corpus: this.turnCorpus(sess, ctx), studentText: utterance, maxWords: 45 });
    let rejected = null;
    if (!v.ok || (scriptLang(reply) && scriptLang(reply) !== lang)) {
      rejected = v.ok ? 'wrong_language' : v.reason + (v.detail ? ':' + v.detail : '');
      source = 'approved-fallback';
      const cur = (S.objectives || []).find(o => o.id === ctx.objectiveId);
      reply = deferred ? '' : characterId === 'surgeon' && cur ? L(cur.explanation, lang) : (lang === 'ar' ? 'معلش، ممكن تعيد سؤالك؟' : 'Sorry, could you say that again?');
    }
    if (deferred) reply = '';  // Unity speaks the validated, approved line
    sess.history[characterId].push({ role: 'user', content: utterance }, { role: 'assistant', content: reply || '(acknowledged)' });
    if (sess.history[characterId].length > 24) sess.history[characterId].splice(0, 2);
    if (reply) this.noteLine(sess, reply);
    return { status: 200, json: { characterId, heard: utterance, text: reply, lang, intent, deferred, source, rejected } };
  }

  /**
   * Character-initiated line. Unity sends approved base text (supervisor rule + hint, explanation, action line, welcome,
   * debrief). approvedOnly → spoken verbatim. Otherwise the AI may rephrase it, but the result must pass grounding
   * against the base text; else the base text is spoken. The base text itself must be grounded in the scenario corpus.
   */
  async say(sess, body) {
    const S = sess.entry.data; const characterId = CHARACTERS.includes(body.characterId) ? body.characterId : 'surgeon';
    const lang = body.language === 'ar' ? 'ar' : body.language === 'en' ? 'en' : sess.lang;
    const base = String(body.baseText || '').slice(0, 1200).trim();
    if (!base) return { status: 400, json: { error: 'empty_base_text' } };
    const kind = String(body.kind || 'line').slice(0, 24);
    const isDebrief = kind === 'debrief';
    // Debrief numbers come from the session's own counts (facts payload): they are the only numbers allowed in addition.
    const f = isDebrief && body.facts && typeof body.facts === 'object' ? body.facts : null;
    const factNums = f ? [f.completed?.length, f.missed?.length, (f.completed?.length || 0) + (f.missed?.length || 0), f.hints, f.interventions, f.score, f.maxScore,
      Math.round((f.durationSeconds || 0) / 60), Math.round(f.durationSeconds || 0), ...(Array.isArray(f.rejectionCounts) ? f.rejectionCounts : [])].filter(x => Number.isFinite(x)).map(String) : [];
    const g = checkSurgicalGrounding(base, { corpus: [...scenarioCorpus(S), factNums.join(' ')], maxWords: isDebrief ? 140 : 90 });
    if (!g.ok) return { status: 422, json: { error: 'base_text_not_grounded', reason: g.reason, detail: g.detail } };
    let text = base, source = 'approved', rejected = null;
    if (!body.approvedOnly && this.ai.configured) {
      try {
        const P = (S.personas || []).find(p => p.id === characterId) || {};
        const r = await this.ai.json({
          system: `You are ${L(P.displayName, 'en')}. ${P.speechStyle}. Rephrase the APPROVED line below naturally for speech, in ${lang === 'ar' ? 'spoken Palestinian Arabic' : 'English'}, keeping EVERY fact, step name, instrument and number exactly; add nothing. ${isDebrief ? 'Up to 6 short sentences.' : 'At most 2 short sentences.'}\nAPPROVED: "${base}"`,
          messages: [{ role: 'user', content: 'Say it.' }], schema: { type: 'object', additionalProperties: false, required: ['line'], properties: { line: { type: 'string' } } }, name: 'surgery_say', maxOutputTokens: isDebrief ? 400 : 160, tag: 'surgery-say' });
        const cand = cleanReply(r.data.line);
        const v = checkSurgicalGrounding(cand, { corpus: [base], maxWords: isDebrief ? 140 : 60 });
        if (v.ok && (!scriptLang(cand) || scriptLang(cand) === lang)) { text = cand; source = 'ai'; } else rejected = v.ok ? 'wrong_language' : v.reason;
      } catch (e) { rejected = 'ai_unavailable'; }
    }
    this.noteLine(sess, text);
    return { status: 200, json: { characterId, kind, text, lang, source, rejected } };
  }

  // ------------------------------------------------------------------ native voice (same protocol as /v1/voice)
  understandingFor(sess, characterId) {
    this.rtu ??= new Map(); const key = sess.id + ':' + characterId; let rt = this.rtu.get(key);
    if (!rt || rt.closed) { rt = new RealtimeUnderstanding({ apiKey: this.ai.apiKey, model: process.env.MEDSIM_VOICE_UNDERSTAND_MODEL || 'gpt-realtime-2.1-mini', ledger: this.ai.ledger, timeoutMs: 20000 }); this.rtu.set(key, rt); }
    return rt;
  }
  prewarm(sess) {
    if (!this.ai.configured) return;
    for (const c of CHARACTERS) { this.understandingFor(sess, c).connect().catch(() => { }); this.ai.prewarmVoice?.(SURGERY_VOICES[c]); }
  }
  async voiceTurn(sess, characterId, pcm, body, { onMeta, onAudio, signal }) {
    const rt = this.understandingFor(sess, characterId); const t0 = Date.now(); let callId = null, understandMs = 0;
    const understand = async ({ system, schema }) => {
      const sch = JSON.parse(JSON.stringify(schema));
      sch.properties = { spokenLanguage: { type: 'string', enum: ['arabic', 'english', 'mixed', 'none'], description: 'Decide FIRST from the audio: the language the student spoke. none = no intelligible speech.' }, heard: { type: 'string', description: 'Verbatim transcript of what the student said, in the script of the language spoken (Arabic in Arabic script, Palestinian colloquial included). Never translate. Empty if nothing intelligible was said to you.' }, ...sch.properties };
      sch.required = ['spokenLanguage', 'heard', ...(sch.required || [])];
      const r = await rt.understand(pcm, { instructions: system + '\nThe student\'s latest turn is the audio you just received. Identify the language, transcribe it in "heard", then classify and reply.', schema: sch });
      callId = r.callId; understandMs = r.ms;
      if (isNoSpeech(r.data.heard) || r.data.spokenLanguage === 'none' || this.isEcho(sess, r.data.heard)) { const e = new Error('no_speech'); e.code = 'no_speech'; throw e; }
      return r;
    };
    let res;
    try { res = await this.turn(sess, { ...body, characterId, utterance: '', _understand: understand }); }
    catch (e) {
      if (e.code !== 'no_speech') throw e;
      rt.noteSpoken(callId, ''); onMeta({ characterId, heard: '', text: '', noSpeech: true, source: 'no-speech', timings: { understandMs } });
      return { firstAudioMs: null, totalMs: Date.now() - t0, characterId, source: 'no-speech' };
    }
    rt.noteSpoken(callId, res.json.text);
    const tMeta = Date.now(); onMeta({ ...res.json, timings: { understandMs, validateMs: tMeta - t0 - understandMs } });
    if (!res.json.text) return { firstAudioMs: null, totalMs: Date.now() - t0, characterId, source: res.json.source, deferred: res.json.deferred };
    const out = await this.streamSpeech(sess, characterId, res.json.text, { onAudio, signal, t0, tMeta, kind: 'surgery-voice', lang: res.json.lang });
    return { ...out, characterId, source: res.json.source };
  }
  async sayStream(sess, body, { onMeta, onAudio, signal }) {
    const t0 = Date.now(); const r = await this.say(sess, body);
    if (r.status !== 200) { onMeta(r.json); return null; }
    const tMeta = Date.now(); onMeta(r.json);
    const out = await this.streamSpeech(sess, r.json.characterId, r.json.text, { onAudio, signal, t0, tMeta, kind: 'surgery-say-' + r.json.kind, lang: r.json.lang, severity: body.severity, debrief: r.json.kind === 'debrief' });
    return { ...out, characterId: r.json.characterId, source: r.json.source };
  }
  async streamSpeech(sess, characterId, text, { onAudio, signal, t0, tMeta, kind, lang, severity, debrief }) {
    const style = characterId === 'assistant' ? STYLE.assistant : debrief ? STYLE.surgeonDebrief : severity === 'safety' ? STYLE.surgeonSafety : STYLE.surgeon;
    const sp = await this.ai.speak(text, { voice: SURGERY_VOICES[characterId], style, lang: scriptLang(text) || lang, onAudio, signal });
    const fidelity = sp.cancelled ? null : speechFidelity(text, sp.transcript);
    const firstAudioMs = sp.firstAudioMs != null ? (tMeta - t0) + sp.firstAudioMs : null;
    this.ai.ledger.record({ kind: kind + '-latency', model: 'pipeline', ms: Date.now() - t0, firstAudioMs, usd: 0, fidelity, cancelled: !!sp.cancelled, lang });
    return { firstAudioMs, totalMs: Date.now() - t0, fidelity, cancelled: !!sp.cancelled };
  }
}
