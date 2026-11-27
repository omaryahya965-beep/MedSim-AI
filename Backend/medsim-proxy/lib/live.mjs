// Live AI conversation engine for the MedSim proxy.
// Two independent personas (patient, supervising doctor) with separate server-side histories, roles, voices and
// knowledge. Every generated line passes deterministic grounding checks (lib/grounding.mjs) before it is spoken; if it
// fails, approved scenario text is spoken instead. All speech is synthesised at runtime (no pre-recorded dialogue).
import crypto from 'node:crypto';
import { validateGrounded, matchConcepts } from './grounding.mjs';
import { detectLang, scriptLang, detectLanguageCommand, resolveReplyLang, replyLanguageRule, LANGUAGE_ACK } from './language.mjs';
import { speechFidelity, pcmToWav, RealtimeUnderstanding } from './openai.mjs';

export const VOICES = {
  Patient: { voice: process.env.MEDSIM_VOICE_PATIENT || 'cedar' },
  Instructor: { voice: process.env.MEDSIM_VOICE_INSTRUCTOR || 'marin' },
};

const PATIENT_STYLE = {
  discomfort: 'a 58-year-old working man with heavy chest pain: worried, a little breathless, short pauses, low voice, plain everyday speech',
  distress: 'a 58-year-old man in persistent chest pain: anxious, sweaty, breathing faster, words come in short bursts',
  severe_distress: 'a 58-year-old man who feels much worse: frightened, faint, breathless, speaking with effort in short phrases',
  anxious_reassured: 'a 58-year-old man still in pain but a little relieved that help is coming: tired, grateful, quieter',
};
const INSTRUCTOR_STYLE = {
  default: 'a calm, warm emergency physician in her forties teaching a junior doctor at the bedside: clear, kind, unhurried, quietly confident',
  safety: 'a calm emergency physician who needs the student to act now: focused and firm but supportive, slightly quicker pace',
  debrief: 'a warm, encouraging clinical teacher giving personal feedback after a session: measured, specific, supportive',
};

const PATIENT_ACTS = ['greeting', 'introduction', 'identity_check', 'consent_request', 'history_question', 'reassurance', 'explanation', 'examination_notice', 'question_about_diagnosis', 'current_symptoms_question', 'small_talk', 'other'];
const INSTRUCTOR_INTENTS = ['greeting', 'hint_request', 'investigation_request', 'escalation_request', 'diagnosis_statement', 'diagnostic_question', 'summary_or_reasoning', 'question_about_case', 'smalltalk', 'out_of_scope'];

/** Removes stray JSON/quote debris the model occasionally leaves around a reply (e.g. a trailing "}). */
export function cleanReply(x) {
  return String(x || '').trim().replace(/^[\s"“”'{\[]+/, '').replace(/[\s"“”{}\[\]]+$/, '').replace(/\s{2,}/g, ' ').trim();
}
/** True when the Realtime session reports no intelligible student speech (noise, echo of the characters, silence). */
export function isNoSpeech(h) {
  const t = String(h || '').trim().toLowerCase();
  return !t || /^[<\[(].*[>\])]$/.test(t) || /^(unknown|inaudible|unintelligible|silence|noise|no speech|\.+|…)$/.test(t);
}

export class LiveEngine {
  constructor({ ai, scenarios, limits = {} }) {
    this.ai = ai; this.scenarios = scenarios;
    this.sessions = new Map(); this.audio = new Map();
    this.limits = { maxTurnsPerSession: 160, perMinute: 40, maxUtterance: 600, maxSpeakChars: 700, sessionTtlMs: 3 * 3600e3, audioTtlMs: 180e3, ...limits };
    this.minuteWindow = [];
  }

  // ------------------------------------------------------------------ sessions & limits
  start({ scenarioId, scenarioVersion, language }) {
    const entry = this.scenarios.get(scenarioId);
    if (!entry) return { status: 404, json: { error: 'unknown_scenario' } };
    if (scenarioVersion && scenarioVersion !== entry.data.version) return { status: 409, json: { error: 'scenario_version_mismatch', serverVersion: entry.data.version } };
    this.gc();
    const id = crypto.randomUUID();
    this.sessions.set(id, { id, scenarioId, entry, lang: language === 'ar' ? 'ar' : 'en', created: Date.now(), turns: 0, history: { Patient: [], Instructor: [] } });
    return { status: 200, json: { sessionId: id, scenarioVersion: entry.data.version, voices: { patient: VOICES.Patient.voice, instructor: VOICES.Instructor.voice } } };
  }
  end(sessionId) { this.sessions.delete(sessionId); for (const k of [...(this.rtu?.keys() || [])]) if (k.startsWith(sessionId + ':')) { this.rtu.get(k).close(); this.rtu.delete(k); } return { status: 200, json: { ok: true } }; }
  gc() {
    const now = Date.now();
    for (const [k, s] of this.sessions) if (now - s.created > this.limits.sessionTtlMs) this.sessions.delete(k);
    for (const [k, a] of this.audio) if (now - a.created > this.limits.audioTtlMs) this.audio.delete(k);
  }
  admit(sess) {
    const now = Date.now();
    this.minuteWindow = this.minuteWindow.filter(t => now - t < 60e3);
    if (this.minuteWindow.length >= this.limits.perMinute) return 'rate_limited';
    if (sess && sess.turns >= this.limits.maxTurnsPerSession) return 'session_turn_limit';
    this.minuteWindow.push(now); if (sess) sess.turns++;
    return null;
  }
  get(sessionId) { return this.sessions.get(sessionId); }

  // ------------------------------------------------------------------ helpers
  langOf(sess, body) { return body.language === 'ar' ? 'ar' : body.language === 'en' ? 'en' : sess.lang; }
  tr(sess, lang, kind, id, fallback) { const t = lang === 'ar' ? sess.entry.translations?.ar?.[kind]?.[id] : null; return t || fallback; }
  state(sess, ctx) { const S = sess.entry.data; return (S.patientStates || []).find(x => x.id === ctx.stateId) || (S.patientStates || [])[0] || null; }
  sanitizeContext(sess, c = {}) {
    const S = sess.entry.data;
    const valid = new Set([...(S.historyFacts || []).map(f => 'FACT:' + f.id), ...(S.examFindings || []).map(e => 'EXAM:' + e.id), ...(S.investigations || []).map(i => 'INV:' + i.id), ...(S.reasoningConcepts || []).map(x => 'CONCEPT:' + x.id),
      'ECG_REVIEWED', 'ESCALATION_REQUESTED', 'SUMMARY_GIVEN', 'HINT_REQUESTED', 'HANDS_SANITIZED', 'INSTRUMENT_ON_PATIENT', 'ZONE_REJECTED', 'INSTRUMENT_REMOVED_EARLY', 'DIAGNOSIS_NAMED', 'DIAGNOSTIC_QUESTION', 'DOOR_OPENED', 'ENTERED_ROOM']);
    const events = (Array.isArray(c.events) ? c.events : []).map(String).filter(e => valid.has(e) || e.startsWith('INTERVENTION:')).slice(0, 80);
    const stages = ['Introduction', 'HistoryTaking', 'InitialAssessment', 'PhysicalExamination', 'Investigations', 'ClinicalReasoning', 'SupervisorFeedback', 'Debriefing'];
    return { stage: stages.includes(c.stage) ? c.stage : 'Introduction', stateId: String(c.stateId || ''), events, discussionId: String(c.discussionId || ''), missing: Array.isArray(c.missing) ? c.missing.map(String).slice(0, 30) : [] };
  }
  revealedTexts(sess, ctx, lang) {
    const S = sess.entry.data; const out = [];
    const st = this.state(sess, ctx); const v = st && !st.inheritVitals ? st.vitals : S.vitals;
    for (const e of ctx.events) {
      if (e.startsWith('EXAM:')) {
        const ex = S.examFindings.find(x => 'EXAM:' + x.id === e); if (!ex) continue;
        if (ex.id === 'EX_BP') out.push(`Blood pressure ${v.systolic}/${v.diastolic} mmHg, pulse ${v.heartRate} per minute.`);
        else if (ex.id === 'EX_SPO2') out.push(`Oxygen saturation ${v.spo2} percent on room air, pulse ${v.heartRate}.`);
        else out.push(ex.finding);
      } else if (e.startsWith('INV:')) {
        const inv = S.investigations.find(x => 'INV:' + x.id === e); if (inv) out.push(`${inv.name}: ${inv.result}`);
      }
    }
    if (ctx.events.includes('INV:INV_ECG')) out.push('ECG: ST elevation in the inferior leads II, III and aVF, with reciprocal ST depression in I and aVL.');
    for (const e of ctx.events) if (e.startsWith('FACT:')) { const f = S.historyFacts.find(x => 'FACT:' + x.id === e); if (f) out.push(`Patient said: ${f.answer}`); }
    if (ctx.events.includes('INV:INV_ECG') || ctx.events.includes('EXAM:EX_BP')) out.push(`Monitor: heart rate ${v.heartRate}, respiratory rate ${v.respiratoryRate}.`);
    return out;
  }

  /** English approved line → approved Arabic wording (scenario translation file). Exact, or segment-wise for composites. */
  arMap(entry) {
    if (entry._arMap) return entry._arMap;
    const m = new Map(); const S = entry.data, T = entry.translations?.ar || {};
    const add = (en, ar) => { if (en && ar && typeof ar === 'string') m.set(String(en).trim(), ar); };
    for (const [k, v] of Object.entries(T.instructorLines || {})) add(S.instructorLines?.[k], v);
    for (const h of S.hints || []) add(h.text, T.hints?.[`${h.stage}|${h.order}`]);
    for (const i of S.interventions || []) (i.lines || []).forEach((l, ix) => add(l, T.interventions?.[`${i.id}|${ix}`]));
    for (const d of S.discussion || []) add(d.question, T.discussion?.[d.id]);
    for (const f of S.historyFacts || []) add(f.answer, T.facts?.[f.id]);
    for (const st of S.patientStates || []) (st.statements || []).forEach((l, ix) => add(l, (T.states?.[st.id] || [])[ix]));
    entry._arMap = m; return m;
  }
  translateLine(entry, text) {
    const t = String(text || '').trim(); if (!t) return null;
    const m = this.arMap(entry); if (m.has(t)) return m.get(t);
    let out = t; let hit = false;
    for (const [en, ar] of [...m.entries()].sort((a, b) => b[0].length - a[0].length)) { if (out.includes(en)) { out = out.split(en).join(ar); hit = true; } }
    return hit && scriptLang(out) === 'ar' ? out : null;
  }
  /** All Arabic approved wording (for bilingual grounding corpora). */
  arCorpus(entry) { return [...this.arMap(entry).values(), ...Object.values(entry.translations?.ar?.unknownAnswers || {})]; }
  langAck(sess, persona, cmd, utterance) {
    const reply = LANGUAGE_ACK[persona][cmd];
    sess.history[persona].push({ role: 'user', content: utterance }, { role: 'assistant', content: reply });
    return { status: 200, json: { persona, heard: utterance, text: reply, lang: cmd, languageCommand: cmd, source: 'language-command', factIds: [], studentAct: 'other', intent: 'smalltalk', investigationId: null, escalationRequested: false, diagnosisNamed: false, isQuestion: false, concepts: [], deterministicConcepts: [], rejected: null, audioId: null, llmMs: 0 } };
  }

  /** Synthesises speech at runtime and returns an audio id (the client fetches /v1/audio/:id). */
  queueSpeech(persona, text, { lang, style }) {
    const id = crypto.randomUUID();
    const voice = VOICES[persona].voice;
    const rec = { created: Date.now(), status: 'pending', wav: null, error: null, fidelity: null, engine: null };
    rec.promise = (async () => {
      try {
        let r = await this.ai.speak(text, { voice, style, lang });
        rec.fidelity = speechFidelity(text, r.transcript); rec.engine = 'realtime';
        if (rec.fidelity < 0.75) { // the voice model drifted from the validated text → re-synthesise with the exact-text engine
          try { r = await this.ai.speakLegacy(text, { voice, style }); rec.engine = 'speech-endpoint(fidelity-retry)'; } catch { }
        }
        rec.wav = pcmToWav(r.pcm); rec.status = 'ready'; rec.firstAudioMs = r.firstAudioMs; rec.ms = r.ms;
      } catch (e) {
        try { const r = await this.ai.speakLegacy(text, { voice, style }); rec.wav = pcmToWav(r.pcm); rec.status = 'ready'; rec.engine = 'speech-endpoint(fallback)'; }
        catch (e2) { rec.status = 'failed'; rec.error = e.code === 'budget_exceeded' ? 'budget_exceeded' : 'tts_unavailable'; }
      }
    })();
    this.audio.set(id, rec);
    return id;
  }
  async audioFor(id) { const a = this.audio.get(id); if (!a) return null; await a.promise; this.audio.delete(id); return a; }

  // ------------------------------------------------------------------ patient
  async patientTurn(sess, body) {
    const S = sess.entry.data; const conv = this.langOf(sess, body);
    const lock = body.langLock === 'ar' || body.langLock === 'en' ? body.langLock : null;
    let utterance = String(body.utterance || '').slice(0, this.limits.maxUtterance);
    if (!body._understand) { const cmd = detectLanguageCommand(utterance); if (cmd) return this.langAck(sess, 'Patient', cmd, utterance); }
    // Reply language: known up front for typed turns; for voice it is decided by what is heard (adaptive prompt rule).
    let lang = body._understand ? (lock || conv) : resolveReplyLang({ lock, heard: utterance, conversation: conv });
    const bilingual = !!body._understand || lang === 'ar';
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const disclosed = new Set(ctx.events.filter(e => e.startsWith('FACT:')).map(e => e.slice(5)));
    const P = S.personas?.patient || {};
    const arF = id => this.tr(sess, 'ar', 'facts', id, '');
    const factLines = S.historyFacts.map(f => `- ${f.id} [topic: ${f.keywords.slice(0, 4).join(', ')}]${disclosed.has(f.id) ? ' (already told)' : ''}: "${f.answer}"${bilingual && arF(f.id) ? ` / Arabic: «${arF(f.id)}»` : ''}`).join('\n');
    const arStates = st ? (sess.entry.translations?.ar?.states?.[st.id] || []) : [];
    const stateLines = st ? st.statements.map((x, i) => `- "${x}"${bilingual && arStates[i] ? ` / Arabic: «${arStates[i]}»` : ''}`).join('\n') : '';
    const sys = langRule => `You are role-playing a SIMULATED patient for medical students. Stay in character.
Who you are: ${P.summary}
How you speak: ${P.speechStyle}
Boundaries: ${P.boundaries}
How you feel right now (${st?.label || 'current state'}): ${st?.context || ''} Pain about ${st?.painScore > 0 ? st.painScore : 'the same'} out of ten.
Things you may say about how you feel now:\n${stateLines}
Your approved facts (the ONLY medical information you know; id [topic]: your words):\n${factLines}
Rules:
- ${langRule} At most 2 short sentences (max 35 words). No lists. Answer exactly what was asked, including short follow-ups ("and before that?", "وكمان؟") using the conversation so far.
- If the student asks about something covered by a fact, choose that fact id in factIds and convey it faithfully in your own natural words: keep every detail identical (times, numbers, drug names, body parts, relatives). Do not add details that are not in the fact.
- You may express emotion, react to greetings, reassurance, consent requests or examination naturally, but do not add new symptoms or medical details beyond the facts and "how you feel" lines.
- If asked how you feel now, describe it using the "how you feel" lines (studentAct current_symptoms_question).
- If nothing you know answers the question, say naturally that you don't know or aren't sure, and choose no fact.
- Never name or guess a diagnosis, never give test results or measurements, never mention a simulation, AI, scores or grading.`;
    const system = sys(body._understand ? replyLanguageRule('Patient', { lock }) : replyLanguageRule('Patient', { lock, known: lang }));
    const schema = { type: 'object', additionalProperties: false, required: ['factIds', 'studentAct', 'emotion', 'reply'], properties: {
      factIds: { type: 'array', maxItems: 3, items: { type: 'string', enum: S.historyFacts.map(f => f.id) } },
      studentAct: { type: 'string', enum: PATIENT_ACTS },
      emotion: { type: 'string', enum: ['pain', 'worried', 'relieved', 'neutral', 'distressed', 'grateful'] },
      reply: { type: 'string' } } };
    const messages = [...sess.history.Patient.slice(-12), { role: 'user', content: utterance }];
    const t0 = Date.now();
    let r = body._understand ? await body._understand({ system, schema }) : await this.ai.json({ system, messages, schema, name: 'patient_turn', maxOutputTokens: 300, tag: 'patient' });
    if (body._understand) {
      utterance = String(r.data.heard || '').slice(0, this.limits.maxUtterance);
      const cmd = detectLanguageCommand(utterance); if (cmd) return this.langAck(sess, 'Patient', cmd, utterance);
      lang = resolveReplyLang({ lock, heard: utterance, conversation: conv, spoken: r.data.spokenLanguage });
    }
    // The reply MUST be in the conversation language: one regeneration with the language fixed, else approved text.
    if (scriptLang(r.data.reply) && scriptLang(r.data.reply) !== lang) {
      try { r = await this.ai.json({ system: sys(replyLanguageRule('Patient', { lock, known: lang })), messages: [...sess.history.Patient.slice(-12), { role: 'user', content: utterance }], schema, name: 'patient_turn', maxOutputTokens: 300, tag: 'patient-lang' }); } catch (e) { if (e.code === 'budget_exceeded') throw e; }
    }
    const valid = new Set(S.historyFacts.map(f => f.id));
    const factIds = [...new Set((r.data.factIds || []).filter(id => valid.has(id)))].slice(0, 3);
    const corpus = [P.summary, S.chiefComplaint || '', 'ألم وجع في الصدر بنص صدري', ...(st?.statements || []), st?.context || '', ...arStates,
      ...factIds.flatMap(id => [S.historyFacts.find(f => f.id === id).answer, this.tr(sess, 'ar', 'facts', id, '')]), ...S.unknownAnswers, ...Object.values(sess.entry.translations?.ar?.unknownAnswers || {})];
    let reply = cleanReply(r.data.reply);
    let v = validateGrounded(reply, { corpus, studentText: utterance, maxWords: 45 });
    if (v.ok && scriptLang(reply) && scriptLang(reply) !== lang) v = { ok: false, reason: 'wrong_language', detail: scriptLang(reply) };
    let source = 'ai';
    if (!v.ok) {
      source = 'approved-fallback';
      const stLines = st ? (lang === 'ar' && sess.entry.translations?.ar?.states?.[st.id] ? sess.entry.translations.ar.states[st.id] : st.statements) : [];
      reply = factIds.length ? factIds.map(id => this.tr(sess, lang, 'facts', id, S.historyFacts.find(f => f.id === id).answer)).join(' ')
        : (['current_symptoms_question', 'reassurance', 'greeting', 'examination_notice'].includes(r.data.studentAct) && stLines.length) ? stLines[0]
        : this.tr(sess, lang, 'unknownAnswers', '0', S.unknownAnswers[0]);
    }
    sess.history.Patient.push({ role: 'user', content: utterance }, { role: 'assistant', content: reply });
    const style = PATIENT_STYLE[st?.expression] || PATIENT_STYLE.discomfort;
    const audioId = body.speak === false ? null : this.queueSpeech('Patient', reply, { lang, style });
    return { status: 200, json: { persona: 'Patient', heard: utterance, text: reply, lang, factIds, studentAct: PATIENT_ACTS.includes(r.data.studentAct) ? r.data.studentAct : 'other', emotion: r.data.emotion, source, rejected: v.ok ? null : v.reason + (v.detail ? ':' + v.detail : ''), audioId, llmMs: Date.now() - t0, model: this.ai.models.chat } };
  }

  // ------------------------------------------------------------------ supervising doctor (student addresses her)
  async instructorTurn(sess, body) {
    const S = sess.entry.data; const conv = this.langOf(sess, body);
    const lock = body.langLock === 'ar' || body.langLock === 'en' ? body.langLock : null;
    let utterance = String(body.utterance || '').slice(0, this.limits.maxUtterance);
    if (!body._understand) { const cmd = detectLanguageCommand(utterance); if (cmd) return this.langAck(sess, 'Instructor', cmd, utterance); }
    let lang = body._understand ? (lock || conv) : resolveReplyLang({ lock, heard: utterance, conversation: conv });
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const P = S.personas?.instructor || {};
    const revealed = this.revealedTexts(sess, ctx, lang);
    const feedback = ctx.stage === 'SupervisorFeedback' || ctx.stage === 'Debriefing';
    const dq = (S.discussion || []).find(d => d.id === ctx.discussionId);
    const hintsAr = (S.hints || []).filter(h => h.stage === ctx.stage).map(h => sess.entry.translations?.ar?.hints?.[`${h.stage}|${h.order}`]).filter(Boolean);
    const hints = (S.hints || []).filter(h => h.stage === ctx.stage).map(h => h.text);
    const dqAr = dq ? sess.entry.translations?.ar?.discussion?.[dq.id] : null;
    const sys = langRule => `You are ${P.summary} This is a medical education SIMULATION with a fictional patient.
How you speak: ${P.speechStyle}
Boundaries: ${P.boundaries}
Current stage: ${ctx.stage}. Patient condition: ${st?.label || ''} — ${st?.context || ''}
Findings the student has obtained so far (you may refer ONLY to these):\n${revealed.map(x => '- ' + x).join('\n') || '- none yet'}
Approved coaching hints for this stage (use their ideas when the student asks for help):\n${hints.map(h => '- ' + h).join('\n') || '- (none)'}${hintsAr.length ? '\nSame hints in Arabic:\n' + hintsAr.map(h => '- ' + h).join('\n') : ''}
${dq ? `You are in the clinical discussion. Your current question to the student was: "${dq.question}"${dqAr ? ` (Arabic: «${dqAr}»)` : ''}. Judge their answer, then either acknowledge briefly or ask ONE short follow-up that guides without giving the answer.` : ''}
Classify the student's message and reply as Dr. Hassan.
Rules:
- ${langRule} 1-2 short sentences (max 40 words). Respond directly to what the student actually said (including short follow-ups, uncertainty or worries) using the conversation so far.
- ${feedback ? 'You may discuss the working diagnosis now.' : 'Do NOT state or confirm a diagnosis; ask what they think instead.'}
- Never invent findings, results, numbers or doses. Only mention numbers that appear in the obtained findings above.
- If they request an investigation, set intent investigation_request and investigationId; acknowledge you will arrange it (do not state its result).
- escalationRequested = true ONLY if the student explicitly asks/proposes to call, page, contact or escalate to a senior/cardiology/cath lab (not merely naming a diagnosis).
- diagnosisNamed = true if the student states a diagnosis as their impression; isQuestion = true if they are asking you a question.
- concepts: which of these reasoning concepts the student's words clearly express: ${(S.reasoningConcepts || []).map(c => `${c.id} (${c.label})`).join('; ')}.
- If out of scope (real patients, dosing, anything outside the case), say kindly it is outside this simulation and to ask their clinical tutor.
- Teach by asking, not telling: never give management instructions, never advise waiting for, delaying or withholding treatment, results or escalation.`;
    const system = sys(body._understand ? replyLanguageRule('Instructor', { lock }) : replyLanguageRule('Instructor', { lock, known: lang }));
    const conceptIds = (S.reasoningConcepts || []).map(c => c.id);
    const schema = { type: 'object', additionalProperties: false, required: ['intent', 'investigationId', 'escalationRequested', 'diagnosisNamed', 'isQuestion', 'concepts', 'reply'], properties: {
      intent: { type: 'string', enum: INSTRUCTOR_INTENTS },
      investigationId: { type: ['string', 'null'], enum: [...S.investigations.map(i => i.id), null] },
      escalationRequested: { type: 'boolean' }, diagnosisNamed: { type: 'boolean' }, isQuestion: { type: 'boolean' },
      concepts: { type: 'array', items: { type: 'string', enum: conceptIds.length ? conceptIds : ['NONE'] } },
      reply: { type: 'string' } } };
    const messages = [...sess.history.Instructor.slice(-12), { role: 'user', content: utterance }];
    const t0 = Date.now();
    let r = body._understand ? await body._understand({ system, schema }) : await this.ai.json({ system, messages, schema, name: 'instructor_turn', maxOutputTokens: 350, tag: 'instructor' });
    if (body._understand) {
      utterance = String(r.data.heard || '').slice(0, this.limits.maxUtterance);
      const cmd = detectLanguageCommand(utterance); if (cmd) return this.langAck(sess, 'Instructor', cmd, utterance);
      lang = resolveReplyLang({ lock, heard: utterance, conversation: conv, spoken: r.data.spokenLanguage });
    }
    if (scriptLang(r.data.reply) && scriptLang(r.data.reply) !== lang) {
      try { r = await this.ai.json({ system: sys(replyLanguageRule('Instructor', { lock, known: lang })), messages: [...sess.history.Instructor.slice(-12), { role: 'user', content: utterance }], schema, name: 'instructor_turn', maxOutputTokens: 350, tag: 'instructor-lang' }); } catch (e) { if (e.code === 'budget_exceeded') throw e; }
    }
    const d = r.data;
    const intent = INSTRUCTOR_INTENTS.includes(d.intent) ? d.intent : 'smalltalk';
    const investigationId = intent === 'investigation_request' && S.investigations.some(i => i.id === d.investigationId) ? d.investigationId : null;
    const corpus = [P.summary, ...revealed, ...hints, ...hintsAr, ...this.arCorpus(sess.entry), ...S.investigations.map(i => i.name), ...S.objectives.map(o => o.text), ...(S.hints || []).map(h => h.text), ...(S.interventions || []).flatMap(i => i.lines), ...(S.discussion || []).map(q => q.question),
      S.instructorLines?.outOfScope || '', S.instructorLines?.escalationAck || '', ...(investigationId ? [S.investigations.find(i => i.id === investigationId).name] : []),
      ...(feedback ? [S.expectedReasoning.workingDiagnosis, ...S.expectedReasoning.keyPoints] : [])];
    let reply = cleanReply(d.reply);
    let v = validateGrounded(reply, { corpus, studentText: utterance, allowDiagnosis: feedback, maxWords: 55, allowMeta: true });
    if (v.ok && scriptLang(reply) && scriptLang(reply) !== lang) v = { ok: false, reason: 'wrong_language', detail: scriptLang(reply) };
    let source = 'ai';
    if (!v.ok) {
      source = 'approved-fallback';
      const ar = lang === 'ar', FB = sess.entry.translations?.ar?.instructorFallbacks || {};
      const L = k => (ar ? sess.entry.translations?.ar?.instructorLines?.[k] : null) || S.instructorLines[k];
      if (investigationId) {
        const inv = S.investigations.find(i => i.id === investigationId); const nameAr = sess.entry.translations?.ar?.investigations?.[inv.id];
        reply = ar && nameAr ? (inv.available ? (FB.investigationArranged || 'تمام، رح أطلب {name} هلّق.').replace('{name}', nameAr) : (FB.investigationUnavailable || '{name} مش متوفر هون هلّق.').replace('{name}', nameAr))
          : (inv.available ? `Good. I'll arrange the ${inv.name.toLowerCase()} now.` : `A ${inv.name.toLowerCase()} isn't available here right now.`);
      }
      else if (intent === 'hint_request') reply = (ar ? hintsAr[0] : hints[0]) || L('noHintAvailable');
      else if (intent === 'out_of_scope') reply = L('outOfScope');
      else if (d.escalationRequested) reply = L('escalationAck');
      else if (intent === 'diagnostic_question' || d.diagnosisNamed) reply = ar ? (FB.whatDoYouThink || 'شو رأيك إنت؟ وشو اللي بيدعم هالتفكير؟') : 'What do you think it is, and what supports that?';
      else if (dq) reply = ar ? (FB.tellMore || 'احكيلي أكتر كيف وصلت لهالفكرة.') : 'Tell me a little more about how you reached that.';
      else if (d.isQuestion) reply = ar ? (FB.goodQuestion || 'سؤال منيح. كيف ممكن تعرف هالإشي وإنت عند السرير؟') : 'Good question. How could you find that out at the bedside?';
      else reply = ar ? (FB.whatNext || 'تمام. شو حابب تعمل هلّق؟') : 'Okay. What would you like to do next?';
    }
    sess.history.Instructor.push({ role: 'user', content: utterance }, { role: 'assistant', content: reply });
    const audioId = body.speak === false ? null : this.queueSpeech('Instructor', reply, { lang, style: INSTRUCTOR_STYLE.default });
    return { status: 200, json: { persona: 'Instructor', heard: utterance, text: reply, lang, intent, investigationId, escalationRequested: !!d.escalationRequested, diagnosisNamed: !!d.diagnosisNamed, isQuestion: !!d.isQuestion,
      concepts: (d.concepts || []).filter(c => conceptIds.includes(c)), deterministicConcepts: matchConcepts(utterance, S.reasoningConcepts || []), source, rejected: v.ok ? null : v.reason + (v.detail ? ':' + v.detail : ''), audioId, llmMs: Date.now() - t0, model: this.ai.models.chat } };
  }

  // ------------------------------------------------------------------ director-initiated speech (welcome, interventions, discussion, results, debrief)
  async say(sess, body) {
    const S = sess.entry.data; const lang = this.langOf(sess, body);
    const kind = String(body.kind || 'line');
    const persona = body.persona === 'Patient' ? 'Patient' : 'Instructor';
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const base = String(body.baseText || '').slice(0, this.limits.maxSpeakChars);
    const P = persona === 'Patient' ? S.personas?.patient : S.personas?.instructor;
    const revealed = this.revealedTexts(sess, ctx, lang);
    const baseAr = lang === 'ar' ? this.translateLine(sess.entry, base) : null;
    let system, corpus, maxWords = 45, allowDiagnosis = false, fallback = baseAr || base, styleKey = 'default';
    const recent = sess.history[persona].slice(-6).map(m => `${m.role === 'user' ? 'Student' : 'You'}: ${m.content}`).join('\n');

    if (kind === 'debrief') {
      const res = body.result || {};
      const list = a => (Array.isArray(a) ? a : []).map(String).slice(0, 30);
      const met = list(res.met), missed = list(res.missed), critical = list(res.missedCritical), interventions = list(res.interventions), aiOnly = list(res.aiInterpreted);
      const facts = [`Escalated to senior team: ${res.escalated ? 'yes' : 'no'}.`, res.ecgSeconds >= 0 ? `ECG obtained at ${Math.round(res.ecgSeconds)} seconds, about ${Math.round(res.ecgSeconds / 60)} minutes (target within 600 seconds, i.e. 10 minutes).` : 'ECG was not obtained (target within 10 minutes).', `Hints requested: ${res.hints | 0}.`];
      system = `You are ${P.summary} You are giving a short, personal SPOKEN debrief to the student after a simulated case. Educational working diagnosis for this case: ${S.expectedReasoning.workingDiagnosis}. Teaching points: ${S.expectedReasoning.keyPoints.join('; ')}.
Session record (the ONLY things you may mention):
What they did well: ${met.join('; ') || 'none recorded'}
What they missed: ${missed.join('; ') || 'nothing major'}
Safety-critical misses: ${critical.join('; ') || 'none'}
Moments you had to step in: ${interventions.join('; ') || 'none'}
Other facts: ${facts.join(' ')}
${aiOnly.length ? 'Items recognised only by AI interpretation (mention they will be checked by the instructor): ' + aiOnly.join('; ') : ''}
Speak ${lang === 'ar' ? 'in natural Arabic' : 'in natural English'}: 4-6 sentences, max 120 words, warm and specific: one or two strengths, the most important misses and why they matter for this patient, and one concrete thing to do differently next time. End by inviting them to look at the detailed report. No numbers except those in the record. No doses.`;
      // at debrief time everything about the case may be discussed: history, findings and results are part of the record
      corpus = [...met, ...missed, ...critical, ...interventions, ...aiOnly, ...facts, ...S.historyFacts.map(f => f.answer), ...S.examFindings.map(e => e.finding), ...S.investigations.map(i => `${i.name}: ${i.result}`), ...this.arCorpus(sess.entry), S.expectedReasoning.workingDiagnosis, ...S.expectedReasoning.keyPoints, ...S.objectives.map(o => o.text), ...S.rubric.map(r => r.description)];
      maxWords = 140; allowDiagnosis = true; styleKey = 'debrief';
      // Arabic fallback in Arabic only (the rubric item names are English, so they are left to the written report)
      if (lang === 'ar') fallback = [met.length ? `أحسنت في ${met.length > 1 ? 'عدة نقاط أساسية' : 'نقطة أساسية'} بالتقييم.` : '', critical.length ? 'بس في إشي مهم كتير للسلامة فاتك، ولازم تنتبهله المرة الجاية.' : (missed.length ? 'وفي كم نقطة فاتتك وبتستاهل تراجعها.' : ''), res.escalated ? 'وكان منيح إنك صعّدت الحالة للفريق الأعلى.' : '', 'اطّلع على التقرير المفصّل لتشوف كل التفاصيل.'].filter(Boolean).join(' ');
      else fallback = [met.length ? `You did well with ${met.slice(0, 2).join(' and ').toLowerCase()}.` : '', critical.length ? `The most important thing you missed: ${critical[0].toLowerCase()}` : (missed.length ? `Next time, remember: ${missed[0].toLowerCase()}` : ''), `For this patient, the key point is: ${S.expectedReasoning.keyPoints[S.expectedReasoning.keyPoints.length - 1].toLowerCase()}.`, 'Have a look at the detailed report when you are ready.'].filter(Boolean).join(' ');
    } else {
      const purpose = { welcome: 'Welcome the student in the corridor and brief the task, using the approved brief below. 2-3 short sentences, under 15 seconds of speech.',
        intervention: 'Say this coaching line to the student now, in your own natural words, keeping exactly the same meaning and the same level of hinting (do not give more of the answer).',
        discussion: 'Ask the student this discussion question in your own natural words. You may briefly connect it to what they just said.',
        result: 'Tell the student this investigation result conversationally. Keep every number and term exactly as given.',
        ack: 'Say this acknowledgement naturally.',
        state: 'React naturally, in character, to how you feel now.' }[kind] || 'Say this line naturally, keeping the same meaning.';
      system = `You are ${P?.summary || ''} ${P?.speechStyle || ''} Boundaries: ${P?.boundaries || ''}
Task: ${purpose}
Approved line/brief: "${kind === 'welcome' ? (S.instructorLines.welcomeBrief || base) : base}"${baseAr ? `\nApproved Arabic wording of the same line (use its meaning and terms): «${baseAr}»` : ''}
${st ? `Patient condition now: ${st.label}.` : ''}
Recent conversation:\n${recent || '(none)'}
Reply ${lang === 'ar' ? (persona === 'Patient' ? 'in natural spoken Palestinian Arabic (colloquial)' : 'in clear, warm, professional spoken Arabic as a Palestinian doctor talks to a student (Levantine colloquial is good)') : 'in natural spoken English'}, ${kind === 'welcome' ? 'max 45 words' : 'max 30 words'}. Do not add any clinical fact, finding, number or diagnosis that is not in the approved line. Output only the words to be spoken.`;
      corpus = [base, baseAr || '', S.instructorLines.welcomeBrief || '', P?.summary || '', ...revealed, ...(st?.statements || []), ...(lang === 'ar' ? (sess.entry.translations?.ar?.states?.[st?.id] || []) : [])];
      maxWords = kind === 'welcome' ? 55 : 40;
      if (kind === 'intervention') styleKey = body.severity === 'safety' ? 'safety' : 'default';
    }
    const schema = { type: 'object', additionalProperties: false, required: ['speech'], properties: { speech: { type: 'string' } } };
    const t0 = Date.now();
    let text = '', source = 'ai', rejected = null;
    try {
      const r = await this.ai.json({ system, messages: [{ role: 'user', content: kind === 'debrief' ? 'Give the debrief now.' : 'Speak now.' }], schema, name: 'speech', maxOutputTokens: kind === 'debrief' ? 500 : 220, tag: 'say-' + kind });
      text = String(r.data.speech || '').trim();
      let v = validateGrounded(text, { corpus, maxWords, allowDiagnosis });
      if (v.ok && scriptLang(text) && scriptLang(text) !== lang) v = { ok: false, reason: 'wrong_language', detail: scriptLang(text) };
      if (!v.ok) { rejected = v.reason + (v.detail ? ':' + v.detail : ''); text = fallback; source = 'approved-fallback'; }
    } catch (e) { if (e.code === 'budget_exceeded') throw e; text = fallback; source = 'approved-fallback'; rejected = 'llm_unavailable'; }
    if (!text) return { status: 422, json: { error: 'nothing_to_say' } };
    sess.history[persona].push({ role: 'assistant', content: text });
    const style = persona === 'Patient' ? (PATIENT_STYLE[st?.expression] || PATIENT_STYLE.discomfort) : INSTRUCTOR_STYLE[styleKey];
    const audioId = body.speak === false ? null : this.queueSpeech(persona, text, { lang, style });
    return { status: 200, json: { persona, kind, text, lang: scriptLang(text) || lang, source, rejected, audioId, llmMs: Date.now() - t0 } };
  }

  // ------------------------------------------------------------------ native voice (Realtime speech-to-speech with grounding)
  /**
   * Student audio → persistent Realtime session (hears the audio directly; returns a constrained function call) →
   * the SAME deterministic grounding/validation as text turns → validated reply streamed as runtime speech.
   * Medically significant content is therefore never spoken unless it passed validation.
   */
  understandingFor(sess, persona) {
    this.rtu ??= new Map();
    const key = sess.id + ':' + persona;
    let rt = this.rtu.get(key);
    if (!rt || rt.closed) { rt = new RealtimeUnderstanding({ apiKey: this.ai.apiKey, model: process.env.MEDSIM_VOICE_UNDERSTAND_MODEL || 'gpt-realtime-2.1-mini', ledger: this.ai.ledger, timeoutMs: 20000 }); this.rtu.set(key, rt); }
    return rt;
  }

  /** Opens the persistent Realtime connections in advance (connecting is not billed) so the first spoken turn is not slower. */
  prewarm(sess) {
    if (!this.ai.configured) return;
    for (const p of ['Patient', 'Instructor']) this.understandingFor(sess, p).connect().catch(() => { });
    for (const p of ['Patient', 'Instructor']) this.ai.prewarmVoice?.(VOICES[p].voice);
  }

  async voiceTurn(sess, persona, pcm, body, { onMeta, onAudio, signal }) {
    const rt = this.understandingFor(sess, persona);
    const t0 = Date.now(); let callId = null, understandMs = 0;
    const understand = async ({ system, schema }) => {
      const sch = JSON.parse(JSON.stringify(schema));
      sch.properties = { spokenLanguage: { type: 'string', enum: ['arabic', 'english', 'mixed', 'none'], description: 'Decide FIRST, from the sound of the audio: the language the student actually spoke (mixed = Arabic with some English words). none = no intelligible speech.' }, heard: { type: 'string', description: 'Verbatim transcript of what the student just said, in the language and script they spoke (Arabic in Arabic script, including Palestinian colloquial; English medical terms they used stay in English). Never translate. Empty string if the audio has no intelligible speech addressed to you (silence, noise, coughing, or only the simulation characters\' own voices).' }, ...sch.properties };
      sch.required = ['spokenLanguage', 'heard', ...(sch.required || [])];
      const r = await rt.understand(pcm, { instructions: system + "\nThe student's latest turn is the audio you just received. First identify the language they spoke (spokenLanguage), then put exactly what they said in \"heard\" in that language's script (empty if nothing intelligible was said) — Arabic speech is transcribed in Arabic, never translated — then classify it and propose your reply.", schema: sch });
      callId = r.callId; understandMs = r.ms;
      if (isNoSpeech(r.data.heard) || r.data.spokenLanguage === 'none') {
        console.log(`  [voice] no speech: spokenLanguage=${r.data.spokenLanguage} heardChars=${String(r.data.heard || '').length} audioSec=${(pcm.length / 48000).toFixed(2)}`); const e = new Error('no_speech'); e.code = 'no_speech'; throw e; }
      return r;
    };
    const turnBody = { ...body, utterance: '', speak: false, _understand: understand };
    let res;
    try { res = persona === 'Patient' ? await this.patientTurn(sess, turnBody) : await this.instructorTurn(sess, turnBody); }
    catch (e) {
      if (e.code !== 'no_speech') throw e;
      rt.noteSpoken(callId, '');                     // nothing said: no reply, no history, nothing spoken
      onMeta({ persona, heard: '', text: '', noSpeech: true, source: 'no-speech', timings: { understandMs } });
      return { firstAudioMs: null, totalMs: Date.now() - t0, fidelity: null, cancelled: false, persona, source: 'no-speech', understandMs };
    }
    rt.noteSpoken(callId, res.json.text);
    const tMeta = Date.now();
    onMeta({ ...res.json, timings: { understandMs, validateMs: tMeta - t0 - understandMs } });
    const out = await this.streamSpeech(sess, persona, res.json.text, body, { onAudio, signal, t0, tMeta, kind: 'voice', lang: res.json.lang });
    return { ...out, persona, source: res.json.source, rejected: res.json.rejected, understandMs };
  }

  /** Director-initiated line (welcome, intervention, discussion, debrief…), phrased + validated, then streamed. */
  async sayStream(sess, body, { onMeta, onAudio, signal }) {
    const t0 = Date.now();
    const r = await this.say(sess, { ...body, speak: false });
    if (r.status !== 200) { onMeta(r.json); return null; }
    const tMeta = Date.now(); onMeta(r.json);
    const out = await this.streamSpeech(sess, r.json.persona, r.json.text, body, { onAudio, signal, t0, tMeta, kind: 'say-' + (body.kind || 'line') });
    return { ...out, persona: r.json.persona, source: r.json.source, rejected: r.json.rejected };
  }

  async streamSpeech(sess, persona, text, body, { onAudio, signal, t0, tMeta, kind, lang: langHint }) {
    const lang = scriptLang(text) || langHint || this.langOf(sess, body); // speak in the language the line is actually written in
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const style = persona === 'Patient' ? (PATIENT_STYLE[st?.expression] || PATIENT_STYLE.discomfort) : (body.kind === 'debrief' ? INSTRUCTOR_STYLE.debrief : body.severity === 'safety' ? INSTRUCTOR_STYLE.safety : INSTRUCTOR_STYLE.default);
    const sp = await this.ai.speak(text, { voice: VOICES[persona].voice, style, lang, onAudio, signal });
    const fidelity = sp.cancelled ? null : speechFidelity(text, sp.transcript);
    const firstAudioMs = sp.firstAudioMs != null ? (tMeta - t0) + sp.firstAudioMs : null;
    this.ai.ledger.record({ kind: kind + '-latency', model: 'pipeline', ms: Date.now() - t0, firstAudioMs, usd: 0, fidelity, cancelled: !!sp.cancelled, lang });
    if (fidelity != null && fidelity < 0.75) console.warn(`[voice] low speech fidelity ${fidelity.toFixed(2)} (validated text was voiced imprecisely)`);
    return { firstAudioMs, totalMs: Date.now() - t0, fidelity, cancelled: !!sp.cancelled };
  }

}
