// Live AI conversation engine for the MedSim proxy.
// Two independent personas (patient, supervising doctor) with separate server-side histories, roles, voices and
// knowledge. Every generated line passes deterministic grounding checks (lib/grounding.mjs) before it is spoken; if it
// fails, approved scenario text is spoken instead. All speech is synthesised at runtime (no pre-recorded dialogue).
import crypto from 'node:crypto';
import { validateGrounded, matchConcepts } from './grounding.mjs';
import { speechFidelity, pcmToWav } from './openai.mjs';

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
  end(sessionId) { this.sessions.delete(sessionId); return { status: 200, json: { ok: true } }; }
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
    const S = sess.entry.data; const lang = this.langOf(sess, body);
    const utterance = String(body.utterance || '').slice(0, this.limits.maxUtterance);
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const disclosed = new Set(ctx.events.filter(e => e.startsWith('FACT:')).map(e => e.slice(5)));
    const P = S.personas?.patient || {};
    const factLines = S.historyFacts.map(f => `- ${f.id} [topic: ${f.keywords.slice(0, 4).join(', ')}]${disclosed.has(f.id) ? ' (already told)' : ''}: "${this.tr(sess, lang, 'facts', f.id, f.answer)}"`).join('\n');
    const stateLines = st ? (lang === 'ar' && sess.entry.translations?.ar?.states?.[st.id] ? sess.entry.translations.ar.states[st.id] : st.statements).map(x => `- "${x}"`).join('\n') : '';
    const system = `You are role-playing a SIMULATED patient for medical students. Stay in character.
Who you are: ${P.summary}
How you speak: ${P.speechStyle}
Boundaries: ${P.boundaries}
How you feel right now (${st?.label || 'current state'}): ${st?.context || ''} Pain about ${st?.painScore > 0 ? st.painScore : 'the same'} out of ten.
Things you may say about how you feel now:\n${stateLines}
Your approved facts (the ONLY medical information you know; id [topic]: your words):\n${factLines}
Rules:
- Reply in ${lang === 'ar' ? 'natural spoken Palestinian Arabic (colloquial), as this patient would' : 'natural spoken English'}; at most 2 short sentences (max 35 words). No lists.
- If the student asks about something covered by a fact, choose that fact id in factIds and convey it faithfully in your own natural words: keep every detail identical (times, numbers, drug names, body parts, relatives). Do not add details that are not in the fact.
- You may express emotion, react to greetings, reassurance, consent requests or examination naturally, but do not add new symptoms or medical details beyond the facts and "how you feel" lines.
- If asked how you feel now, describe it using the "how you feel" lines (studentAct current_symptoms_question).
- If nothing you know answers the question, say naturally that you don't know or aren't sure, and choose no fact.
- Never name or guess a diagnosis, never give test results or measurements, never mention a simulation, AI, scores or grading.`;
    const schema = { type: 'object', additionalProperties: false, required: ['factIds', 'studentAct', 'emotion', 'reply'], properties: {
      factIds: { type: 'array', maxItems: 3, items: { type: 'string', enum: S.historyFacts.map(f => f.id) } },
      studentAct: { type: 'string', enum: PATIENT_ACTS },
      emotion: { type: 'string', enum: ['pain', 'worried', 'relieved', 'neutral', 'distressed', 'grateful'] },
      reply: { type: 'string' } } };
    const messages = [...sess.history.Patient.slice(-12), { role: 'user', content: utterance }];
    const t0 = Date.now();
    const r = await this.ai.json({ system, messages, schema, name: 'patient_turn', maxOutputTokens: 300, tag: 'patient' });
    const valid = new Set(S.historyFacts.map(f => f.id));
    const factIds = [...new Set((r.data.factIds || []).filter(id => valid.has(id)))].slice(0, 3);
    const corpus = [P.summary, ...(st?.statements || []), st?.context || '', ...(lang === 'ar' && sess.entry.translations?.ar?.states?.[st?.id] ? sess.entry.translations.ar.states[st.id] : []),
      ...factIds.flatMap(id => [S.historyFacts.find(f => f.id === id).answer, this.tr(sess, 'ar', 'facts', id, '')]), ...S.unknownAnswers, ...Object.values(sess.entry.translations?.ar?.unknownAnswers || {})];
    let reply = String(r.data.reply || '').trim();
    const v = validateGrounded(reply, { corpus, studentText: utterance, maxWords: 45 });
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
    return { status: 200, json: { persona: 'Patient', text: reply, factIds, studentAct: PATIENT_ACTS.includes(r.data.studentAct) ? r.data.studentAct : 'other', emotion: r.data.emotion, source, rejected: v.ok ? null : v.reason + (v.detail ? ':' + v.detail : ''), audioId, llmMs: Date.now() - t0, model: this.ai.models.chat } };
  }

  // ------------------------------------------------------------------ supervising doctor (student addresses her)
  async instructorTurn(sess, body) {
    const S = sess.entry.data; const lang = this.langOf(sess, body);
    const utterance = String(body.utterance || '').slice(0, this.limits.maxUtterance);
    const ctx = this.sanitizeContext(sess, body.context);
    const st = this.state(sess, ctx);
    const P = S.personas?.instructor || {};
    const revealed = this.revealedTexts(sess, ctx, lang);
    const feedback = ctx.stage === 'SupervisorFeedback' || ctx.stage === 'Debriefing';
    const dq = (S.discussion || []).find(d => d.id === ctx.discussionId);
    const hints = (S.hints || []).filter(h => h.stage === ctx.stage).map(h => h.text);
    const system = `You are ${P.summary} This is a medical education SIMULATION with a fictional patient.
How you speak: ${P.speechStyle}
Boundaries: ${P.boundaries}
Current stage: ${ctx.stage}. Patient condition: ${st?.label || ''} — ${st?.context || ''}
Findings the student has obtained so far (you may refer ONLY to these):\n${revealed.map(x => '- ' + x).join('\n') || '- none yet'}
Approved coaching hints for this stage (use their ideas when the student asks for help):\n${hints.map(h => '- ' + h).join('\n') || '- (none)'}
${dq ? `You are in the clinical discussion. Your current question to the student was: "${dq.question}". Judge their answer, then either acknowledge briefly or ask ONE short follow-up that guides without giving the answer.` : ''}
Classify the student's message and reply as Dr. Hassan.
Rules:
- Reply in ${lang === 'ar' ? 'natural spoken Arabic (Palestinian/Levantine colloquial is fine)' : 'natural spoken English'}, 1-2 short sentences (max 40 words).
- ${feedback ? 'You may discuss the working diagnosis now.' : 'Do NOT state or confirm a diagnosis; ask what they think instead.'}
- Never invent findings, results, numbers or doses. Only mention numbers that appear in the obtained findings above.
- If they request an investigation, set intent investigation_request and investigationId; acknowledge you will arrange it (do not state its result).
- escalationRequested = true ONLY if the student explicitly asks/proposes to call, page, contact or escalate to a senior/cardiology/cath lab (not merely naming a diagnosis).
- diagnosisNamed = true if the student states a diagnosis as their impression; isQuestion = true if they are asking you a question.
- concepts: which of these reasoning concepts the student's words clearly express: ${(S.reasoningConcepts || []).map(c => `${c.id} (${c.label})`).join('; ')}.
- If out of scope (real patients, dosing, anything outside the case), say kindly it is outside this simulation and to ask their clinical tutor.`;
    const conceptIds = (S.reasoningConcepts || []).map(c => c.id);
    const schema = { type: 'object', additionalProperties: false, required: ['intent', 'investigationId', 'escalationRequested', 'diagnosisNamed', 'isQuestion', 'concepts', 'reply'], properties: {
      intent: { type: 'string', enum: INSTRUCTOR_INTENTS },
      investigationId: { type: ['string', 'null'], enum: [...S.investigations.map(i => i.id), null] },
      escalationRequested: { type: 'boolean' }, diagnosisNamed: { type: 'boolean' }, isQuestion: { type: 'boolean' },
      concepts: { type: 'array', items: { type: 'string', enum: conceptIds.length ? conceptIds : ['NONE'] } },
      reply: { type: 'string' } } };
    const messages = [...sess.history.Instructor.slice(-12), { role: 'user', content: utterance }];
    const t0 = Date.now();
    const r = await this.ai.json({ system, messages, schema, name: 'instructor_turn', maxOutputTokens: 350, tag: 'instructor' });
    const d = r.data;
    const intent = INSTRUCTOR_INTENTS.includes(d.intent) ? d.intent : 'smalltalk';
    const investigationId = intent === 'investigation_request' && S.investigations.some(i => i.id === d.investigationId) ? d.investigationId : null;
    const corpus = [P.summary, ...revealed, ...hints, ...S.investigations.map(i => i.name), ...S.objectives.map(o => o.text), ...(S.hints || []).map(h => h.text), ...(S.interventions || []).flatMap(i => i.lines), ...(S.discussion || []).map(q => q.question),
      S.instructorLines?.outOfScope || '', S.instructorLines?.escalationAck || '', ...(investigationId ? [S.investigations.find(i => i.id === investigationId).name] : []),
      ...(feedback ? [S.expectedReasoning.workingDiagnosis, ...S.expectedReasoning.keyPoints] : [])];
    let reply = String(d.reply || '').trim();
    const v = validateGrounded(reply, { corpus, studentText: utterance, allowDiagnosis: feedback, maxWords: 55, allowMeta: true });
    let source = 'ai';
    if (!v.ok) {
      source = 'approved-fallback';
      if (investigationId) { const inv = S.investigations.find(i => i.id === investigationId); reply = inv.available ? `Good. I'll arrange the ${inv.name.toLowerCase()} now.` : `A ${inv.name.toLowerCase()} isn't available here right now.`; }
      else if (intent === 'hint_request') reply = hints[0] || S.instructorLines.noHintAvailable;
      else if (intent === 'out_of_scope') reply = S.instructorLines.outOfScope;
      else if (d.escalationRequested) reply = S.instructorLines.escalationAck;
      else reply = dq ? 'Tell me a little more about how you reached that.' : d.isQuestion ? 'Good question. How could you find that out at the bedside?' : 'Okay. What would you like to do next?';
    }
    sess.history.Instructor.push({ role: 'user', content: utterance }, { role: 'assistant', content: reply });
    const audioId = body.speak === false ? null : this.queueSpeech('Instructor', reply, { lang, style: INSTRUCTOR_STYLE.default });
    return { status: 200, json: { persona: 'Instructor', text: reply, intent, investigationId, escalationRequested: !!d.escalationRequested, diagnosisNamed: !!d.diagnosisNamed, isQuestion: !!d.isQuestion,
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
    let system, corpus, maxWords = 45, allowDiagnosis = false, fallback = base, styleKey = 'default';
    const recent = sess.history[persona].slice(-6).map(m => `${m.role === 'user' ? 'Student' : 'You'}: ${m.content}`).join('\n');

    if (kind === 'debrief') {
      const res = body.result || {};
      const list = a => (Array.isArray(a) ? a : []).map(String).slice(0, 30);
      const met = list(res.met), missed = list(res.missed), critical = list(res.missedCritical), interventions = list(res.interventions), aiOnly = list(res.aiInterpreted);
      const facts = [`Escalated to senior team: ${res.escalated ? 'yes' : 'no'}.`, res.ecgSeconds >= 0 ? `ECG obtained at ${Math.round(res.ecgSeconds)} seconds (target within 600 seconds).` : 'ECG was not obtained.', `Hints requested: ${res.hints | 0}.`];
      system = `You are ${P.summary} You are giving a short, personal SPOKEN debrief to the student after a simulated case. Educational working diagnosis for this case: ${S.expectedReasoning.workingDiagnosis}. Teaching points: ${S.expectedReasoning.keyPoints.join('; ')}.
Session record (the ONLY things you may mention):
What they did well: ${met.join('; ') || 'none recorded'}
What they missed: ${missed.join('; ') || 'nothing major'}
Safety-critical misses: ${critical.join('; ') || 'none'}
Moments you had to step in: ${interventions.join('; ') || 'none'}
Other facts: ${facts.join(' ')}
${aiOnly.length ? 'Items recognised only by AI interpretation (mention they will be checked by the instructor): ' + aiOnly.join('; ') : ''}
Speak ${lang === 'ar' ? 'in natural Arabic' : 'in natural English'}: 4-6 sentences, max 120 words, warm and specific: one or two strengths, the most important misses and why they matter for this patient, and one concrete thing to do differently next time. End by inviting them to look at the detailed report. No numbers except those in the record. No doses.`;
      corpus = [...met, ...missed, ...critical, ...interventions, ...aiOnly, ...facts, S.expectedReasoning.workingDiagnosis, ...S.expectedReasoning.keyPoints, ...S.objectives.map(o => o.text), ...S.rubric.map(r => r.description)];
      maxWords = 140; allowDiagnosis = true; styleKey = 'debrief';
      fallback = [met.length ? `You did well with ${met.slice(0, 2).join(' and ').toLowerCase()}.` : '', critical.length ? `The most important thing you missed: ${critical[0].toLowerCase()}` : (missed.length ? `Next time, remember: ${missed[0].toLowerCase()}` : ''), `For this patient, the key point is: ${S.expectedReasoning.keyPoints[S.expectedReasoning.keyPoints.length - 1].toLowerCase()}.`, 'Have a look at the detailed report when you are ready.'].filter(Boolean).join(' ');
    } else {
      const purpose = { welcome: 'Welcome the student in the corridor and brief the task, using the approved brief below. 2-3 short sentences, under 15 seconds of speech.',
        intervention: 'Say this coaching line to the student now, in your own natural words, keeping exactly the same meaning and the same level of hinting (do not give more of the answer).',
        discussion: 'Ask the student this discussion question in your own natural words. You may briefly connect it to what they just said.',
        result: 'Tell the student this investigation result conversationally. Keep every number and term exactly as given.',
        ack: 'Say this acknowledgement naturally.',
        state: 'React naturally, in character, to how you feel now.' }[kind] || 'Say this line naturally, keeping the same meaning.';
      system = `You are ${P?.summary || ''} ${P?.speechStyle || ''} Boundaries: ${P?.boundaries || ''}
Task: ${purpose}
Approved line/brief: "${kind === 'welcome' ? (S.instructorLines.welcomeBrief || base) : base}"
${st ? `Patient condition now: ${st.label}.` : ''}
Recent conversation:\n${recent || '(none)'}
Reply ${lang === 'ar' ? 'in natural spoken Arabic' : 'in natural spoken English'}, ${kind === 'welcome' ? 'max 45 words' : 'max 30 words'}. Do not add any clinical fact, finding, number or diagnosis that is not in the approved line. Output only the words to be spoken.`;
      corpus = [base, S.instructorLines.welcomeBrief || '', P?.summary || '', ...revealed, ...(st?.statements || [])];
      maxWords = kind === 'welcome' ? 55 : 40;
      if (kind === 'intervention') styleKey = body.severity === 'safety' ? 'safety' : 'default';
    }
    const schema = { type: 'object', additionalProperties: false, required: ['speech'], properties: { speech: { type: 'string' } } };
    const t0 = Date.now();
    let text = '', source = 'ai', rejected = null;
    try {
      const r = await this.ai.json({ system, messages: [{ role: 'user', content: kind === 'debrief' ? 'Give the debrief now.' : 'Speak now.' }], schema, name: 'speech', maxOutputTokens: kind === 'debrief' ? 500 : 220, tag: 'say-' + kind });
      text = String(r.data.speech || '').trim();
      const v = validateGrounded(text, { corpus, maxWords, allowDiagnosis });
      if (!v.ok) { rejected = v.reason + (v.detail ? ':' + v.detail : ''); text = fallback; source = 'approved-fallback'; }
    } catch (e) { if (e.code === 'budget_exceeded') throw e; text = fallback; source = 'approved-fallback'; rejected = 'llm_unavailable'; }
    if (!text) return { status: 422, json: { error: 'nothing_to_say' } };
    sess.history[persona].push({ role: 'assistant', content: text });
    const style = persona === 'Patient' ? (PATIENT_STYLE[st?.expression] || PATIENT_STYLE.discomfort) : INSTRUCTOR_STYLE[styleKey];
    const audioId = body.speak === false ? null : this.queueSpeech(persona, text, { lang, style });
    return { status: 200, json: { persona, kind, text, source, rejected, audioId, llmMs: Date.now() - t0 } };
  }
}
