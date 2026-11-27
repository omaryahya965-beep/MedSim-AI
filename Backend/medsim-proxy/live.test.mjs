// Offline tests for the v0.2 Live AI engine. A fake AI client stands in for OpenAI: no network, no cost.
import test from 'node:test';
import assert from 'node:assert/strict';
import { loadScenarios } from './server.mjs';
import { LiveEngine } from './lib/live.mjs';
import { validateGrounded, extractNumbers, matchConcepts } from './lib/grounding.mjs';
import { Ledger, BudgetExceededError } from './lib/ledger.mjs';
import { parseEnvFile, redact } from './lib/secrets.mjs';
import os from 'node:os'; import path from 'node:path'; import fs from 'node:fs';

const scenarios = loadScenarios();
const S = scenarios.get('ER_CHEST_PAIN_001').data;
function fakeAI(reply) {
  const calls = [];
  return { calls, models: { chat: 'fake' },
    async json(args) { calls.push(args); return { data: typeof reply === 'function' ? reply(args) : reply, ms: 1 }; },
    async speak(text) { return { pcm: Buffer.alloc(4800), transcript: text, ms: 1, firstAudioMs: 1 }; },
    async speakLegacy() { return { pcm: Buffer.alloc(4800), ms: 1 }; } };
}
const newSession = (eng, lang = 'en') => eng.get(eng.start({ scenarioId: 'ER_CHEST_PAIN_001', scenarioVersion: S.version, language: lang }).json.sessionId);
const ctx = (extra = {}) => ({ stage: 'HistoryTaking', stateId: 'S_PRESENTATION', events: [], ...extra });

test('number extraction understands digits, English and Arabic number words', () => {
  assert.ok(extractNumbers('about forty-five minutes').has(45));
  assert.ok(extractNumbers('about forty five minutes').has(45));
  assert.ok(extractNumbers('152/94').has(152));
  assert.ok(extractNumbers('من حوالي خمس وأربعين دقيقة').has(45));
});

test('grounding rejects ungrounded numbers, clinical terms, diagnoses, dosing and meta talk', () => {
  const corpus = ['It started about forty-five minutes ago. I was carrying shopping up the stairs.'];
  assert.equal(validateGrounded('Started maybe 45 minutes back, on the stairs.', { corpus }).ok, true);
  assert.equal(validateGrounded('It started two hours ago.', { corpus }).reason, 'ungrounded_number');
  assert.equal(validateGrounded('It started on the stairs and my arm went numb.', { corpus }).reason, 'ungrounded_clinical_term');
  assert.equal(validateGrounded('I think it is a heart attack.', { corpus }).reason, 'diagnosis_disclosure');
  assert.equal(validateGrounded('Take 300 mg of aspirin.', { corpus }).reason, 'dosing');
  assert.equal(validateGrounded('This simulation is fun.', { corpus }).reason, 'meta_reference');
  // student's own words may be echoed
  assert.equal(validateGrounded('My arm? No, nothing there.', { corpus, studentText: 'Any pain in your arm?' }).ok, true);
});

test('reasoning concepts: STEMI named is a diagnosis concept, not escalation; ECG concept needs lead territory', () => {
  const c = S.reasoningConcepts;
  assert.deepEqual(matchConcepts('I think this is a STEMI', c), ['C_WORKING_DX']);
  assert.ok(!matchConcepts('There is ST elevation', c).includes('C_ECG_FINDING'));
  assert.ok(matchConcepts('ST elevation in II, III and aVF', c).includes('C_ECG_FINDING'));
  assert.ok(matchConcepts('we should call cardiology for the cath lab', c).includes('C_NEXT_STEP'));
});

test('patient: natural grounded reply is kept; fact ids are enum-checked', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ factIds: ['H_ONSET', 'BOGUS'], studentAct: 'history_question', emotion: 'pain', reply: 'About forty-five minutes ago, doctor. I was carrying the shopping up the stairs.' }), scenarios });
  const r = await eng.patientTurn(newSession(eng), { utterance: 'When did this all begin?', context: ctx() });
  assert.equal(r.json.source, 'ai');
  assert.deepEqual(r.json.factIds, ['H_ONSET']);
  assert.ok(r.json.audioId, 'speech queued at runtime');
});

test('patient: invented symptom or wrong number falls back to the approved answer', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ factIds: ['H_ONSET'], studentAct: 'history_question', emotion: 'pain', reply: 'It started three hours ago and I also have a fever.' }), scenarios });
  const r = await eng.patientTurn(newSession(eng), { utterance: 'When did it start?', context: ctx() });
  assert.equal(r.json.source, 'approved-fallback');
  assert.equal(r.json.text, S.historyFacts.find(f => f.id === 'H_ONSET').answer);
});

test('patient: separate history per persona and per session', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ factIds: [], studentAct: 'greeting', emotion: 'worried', reply: 'Hello, doctor.' }), scenarios });
  const s1 = newSession(eng), s2 = newSession(eng);
  await eng.patientTurn(s1, { utterance: 'Hello', context: ctx() });
  assert.equal(s1.history.Patient.length, 2); assert.equal(s1.history.Instructor.length, 0); assert.equal(s2.history.Patient.length, 0);
});

test('patient prompt receives the CURRENT authored state; AI cannot change it', async () => {
  const ai = fakeAI({ factIds: [], studentAct: 'question_about_diagnosis', emotion: 'distressed', reply: 'It is getting worse. I feel really unwell.' });
  const eng = new LiveEngine({ ai, scenarios });
  const r = await eng.patientTurn(newSession(eng), { utterance: 'How are you feeling now?', context: ctx({ stateId: 'S_DETERIORATION' }) });
  assert.match(ai.calls[0].system, /Scenario-defined deterioration/);
  assert.equal(r.json.source, 'ai');
});

test('instructor: diagnosis before feedback is blocked; numbers only from revealed findings', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ intent: 'question_about_case', investigationId: null, escalationRequested: false, diagnosisNamed: false, isQuestion: true, concepts: [], reply: 'Yes, this is an inferior STEMI.' }), scenarios });
  const r = await eng.instructorTurn(newSession(eng), { utterance: 'Is this a STEMI?', context: ctx() });
  assert.equal(r.json.source, 'approved-fallback');
  const eng2 = new LiveEngine({ ai: fakeAI({ intent: 'question_about_case', investigationId: null, escalationRequested: false, diagnosisNamed: false, isQuestion: true, concepts: [], reply: 'His blood pressure was 152/94. What do you make of that?' }), scenarios });
  assert.equal((await eng2.instructorTurn(newSession(eng2), { utterance: 'What was the BP?', context: ctx() })).json.source, 'approved-fallback', 'not yet measured');
  assert.equal((await eng2.instructorTurn(newSession(eng2), { utterance: 'What was the BP?', context: ctx({ events: ['EXAM:EX_BP'] }) })).json.source, 'ai', 'measured → may be quoted');
});

test('instructor: client cannot inject fake event ids or stages', () => {
  const eng = new LiveEngine({ ai: fakeAI({}), scenarios });
  const c = eng.sanitizeContext(newSession(eng), { stage: 'Hacked', events: ['FACT:H_ONSET', 'FACT:NOPE', 'RESULT: troponin 5000'] });
  assert.equal(c.stage, 'Introduction'); assert.deepEqual(c.events, ['FACT:H_ONSET']);
});

test('debrief: grounded in the session record; invented numbers fall back to the deterministic summary', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ speech: 'You scored 97 percent, excellent.' }), scenarios });
  const r = await eng.say(newSession(eng), { kind: 'debrief', persona: 'Instructor', result: { met: ['Requested a 12-lead ECG.'], missed: ['Asked about allergies.'], missedCritical: ['Asked about allergies.'], escalated: true, ecgSeconds: 120, hints: 0 }, context: ctx({ stage: 'Debriefing' }) });
  assert.equal(r.json.source, 'approved-fallback');
  assert.match(r.json.text, /allergies/i);
});

test('rate limits and per-session turn caps', () => {
  const eng = new LiveEngine({ ai: fakeAI({}), scenarios, limits: { perMinute: 2, maxTurnsPerSession: 1 } });
  const s = newSession(eng);
  assert.equal(eng.admit(s), null); assert.equal(eng.admit(s), 'session_turn_limit');
  assert.equal(eng.admit(null), null); assert.equal(eng.admit(null), 'rate_limited');
});

test('ledger enforces a hard budget cap', () => {
  const f = path.join(os.tmpdir(), `medsim-ledger-${Date.now()}.jsonl`);
  const l = new Ledger({ budgetUsd: 0.01, file: f });
  l.record({ kind: 'x', model: 'm', usd: 0.0095 });
  assert.throws(() => l.guard(0.002), BudgetExceededError);
  fs.rmSync(f, { force: true });
});

test('secrets: env-file parsing and redaction never leak keys', () => {
  assert.deepEqual(parseEnvFile('# c\nOPENAI_API_KEY="sk-abc123456789"\nX=1'), { OPENAI_API_KEY: 'sk-abc123456789', X: '1' });
  assert.equal(redact('Authorization: Bearer sk-proj-abcdefghijk123'), 'Authorization: Bearer ***');
  assert.ok(!redact('key sk-proj-abcdefghijk123 used').includes('abcdefghijk'));
});

test('native voice: noise/echo with no intelligible speech gets no reply, no history and no audio', async () => {
  const eng = new LiveEngine({ scenarios, ai: fakeAI({}) });
  const sess = newSession(eng);
  const noted = [];
  eng.understandingFor = () => ({ understand: async () => ({ data: { heard: '<unknown>', studentAct: 'other', factIds: [], emotion: 'neutral', reply: 'I am not sure.' }, callId: 'c1', ms: 5 }), noteSpoken: (id, t) => noted.push(t) });
  let meta = null; let audio = 0;
  const out = await eng.voiceTurn(sess, 'Patient', Buffer.alloc(9600), { context: ctx() }, { onMeta: m => meta = m, onAudio: () => audio++, signal: undefined });
  assert.equal(meta.noSpeech, true); assert.equal(meta.text, ''); assert.equal(audio, 0);
  assert.equal(out.source, 'no-speech');
  assert.equal(sess.history.Patient.length, 0);
});

test('native voice: replies are cleaned of stray JSON debris before validation', async () => {
  const { cleanReply, isNoSpeech } = await import('./lib/live.mjs');
  assert.equal(cleanReply('What supports that?“}'), 'What supports that?');
  assert.equal(cleanReply('"Okay."'), 'Okay.');
  assert.ok(isNoSpeech('[inaudible]') && isNoSpeech('') && !isNoSpeech('Hello doctor'));
});

test('voice actor: a cancelled line\'s late events never end or pollute the next line; long lines are not timed out', async () => {
  const { RealtimeVoice } = await import('./lib/openai.mjs');
  const sockets = [];
  class FakeWS { constructor() { this.sent = []; sockets.push(this); setTimeout(() => this.onopen?.(), 0); }
    send(s) { const m = JSON.parse(s); this.sent.push(m); if (m.type === 'session.update') setTimeout(() => this.emit({ type: 'session.updated' }), 0); }
    emit(m) { this.onmessage?.({ data: JSON.stringify(m) }); } close() { } }
  const orig = globalThis.WebSocket; globalThis.WebSocket = FakeWS;
  try {
    const ledger = { record() { }, guard() { } };
    const rv = new RealtimeVoice({ apiKey: 'k', model: 'm', voice: 'v', ledger, timeoutMs: 200 });
    const ac = new AbortController();
    const pA = rv.say('line A', { signal: ac.signal });
    await new Promise(r => setTimeout(r, 20));
    const ws = sockets[0]; const createA = ws.sent.find(m => m.type === 'response.create');
    ws.emit({ type: 'response.created', response: { id: 'rA', metadata: createA.response.metadata } });
    ws.emit({ type: 'response.output_audio.delta', response_id: 'rA', delta: Buffer.alloc(100).toString('base64') });
    ac.abort();                                   // student interrupts A
    const a = await pA; assert.equal(a.cancelled, true);
    assert.ok(ws.sent.some(m => m.type === 'response.cancel' && m.response_id === 'rA'));
    const pB = rv.say('line B');
    ws.emit({ type: 'error', error: { code: 'response_cancel_not_active' } });             // harmless late error
    ws.emit({ type: 'response.output_audio.delta', response_id: 'rA', delta: Buffer.alloc(50).toString('base64') }); // stale audio
    ws.emit({ type: 'response.done', response: { id: 'rA', status: 'cancelled' } });       // stale done (settles A)
    await new Promise(r => setTimeout(r, 20));
    const createB = ws.sent.filter(m => m.type === 'response.create')[1];
    ws.emit({ type: 'response.created', response: { id: 'rB', metadata: createB.response.metadata } });
    for (let i = 0; i < 6; i++) { ws.emit({ type: 'response.output_audio.delta', response_id: 'rB', delta: Buffer.alloc(480).toString('base64') }); await new Promise(r => setTimeout(r, 60)); } // 360 ms > first-audio timeout
    ws.emit({ type: 'response.done', response: { id: 'rB', status: 'completed', usage: {} } });
    const b = await pB;
    assert.equal(b.pcm.length, 6 * 480, 'line B has exactly its own audio, complete');
  } finally { globalThis.WebSocket = orig; }
});

test('Arabic: an Arabic question gets an Arabic reply even when the app language is English; wrong-language replies are regenerated, then fall back to approved Arabic', async () => {
  let n = 0;
  const eng = new LiveEngine({ ai: fakeAI(() => { n++; return { factIds: ['H_ONSET'], studentAct: 'history_question', emotion: 'pain', reply: 'It started about forty-five minutes ago.' }; }), scenarios });
  const sess = newSession(eng, 'en');
  const r = await eng.patientTurn(sess, { utterance: 'من إيمتى بلش الوجع؟', language: 'en', context: ctx() });
  assert.equal(n, 2, 'English reply to an Arabic question → one regeneration with the language fixed');
  assert.equal(r.json.lang, 'ar');
  assert.equal(r.json.source, 'approved-fallback');
  assert.match(r.json.text, /[؀-ۿ]/, 'approved Arabic wording, never English');
});

test('Arabic: explicit language commands are acknowledged without any AI call and are not clinical turns', async () => {
  let n = 0;
  const eng = new LiveEngine({ ai: fakeAI(() => { n++; return {}; }), scenarios });
  const sess = newSession(eng, 'en');
  for (const [said, lang] of [['احكي عربي', 'ar'], ['خلينا نحكي بالعربي', 'ar'], ['Speak English please', 'en']]) {
    const r = await eng.instructorTurn(sess, { utterance: said, language: 'en', context: ctx() });
    assert.equal(r.json.languageCommand, lang); assert.equal(r.json.source, 'language-command');
    assert.equal(r.json.escalationRequested, false); assert.equal(r.json.investigationId, null);
  }
  assert.equal(n, 0);
});

test('Arabic: locked Arabic answers English input in Arabic; voice turns trust the spoken language over a translated transcript', async () => {
  const ai = fakeAI({ factIds: ['H_ALLERGY'], studentAct: 'history_question', emotion: 'neutral', reply: 'عندي حساسية من البنسلين، طلعلي منه طفح.' }); ai.ledger = { record() { }, guard() { } };
  const eng = new LiveEngine({ ai, scenarios });
  const sess = newSession(eng, 'en');
  const r = await eng.patientTurn(sess, { utterance: 'Are you allergic to anything?', language: 'ar', langLock: 'ar', context: ctx() });
  assert.equal(r.json.lang, 'ar'); assert.equal(r.json.source, 'ai');
  // voice: the Realtime model translated Arabic speech into an English transcript, but identified the language as Arabic
  eng.understandingFor = () => ({ understand: async () => ({ data: { spokenLanguage: 'arabic', heard: "Don't worry, we are with you.", factIds: [], studentAct: 'reassurance', emotion: 'relieved', reply: 'الله يعطيك العافية يا دكتور.' }, callId: 'c', ms: 5 }), noteSpoken() { } });
  let meta = null;
  await eng.voiceTurn(sess, 'Patient', Buffer.alloc(9600), { language: 'en', context: ctx() }, { onMeta: m => meta = m, onAudio() { } });
  assert.equal(meta.lang, 'ar', 'reply in Arabic because the student SPOKE Arabic');
});

test('Arabic: director lines use the approved Arabic wording and fall back to it (never to English) when the AI line fails grounding', async () => {
  const eng = new LiveEngine({ ai: fakeAI({ speech: 'لازم تعطيه أسبرين وتطلب تروبونين هلّق.' }), scenarios });
  const sess = newSession(eng, 'ar');
  const line = S.interventions.find(i => i.id === 'I_ALLERGY_CHECK').lines[0];
  const r = await eng.say(sess, { persona: 'Instructor', kind: 'intervention', baseText: line, language: 'ar', speak: false, context: ctx() });
  assert.equal(r.json.source, 'approved-fallback');
  assert.match(r.json.text, /الحساسية/, 'approved Arabic intervention');
});

test('echo: a "student" transcript that repeats what a character just said is ignored (speaker echo, not the student)', async () => {
  const ai = fakeAI({ factIds: ['H_RADIATION'], studentAct: 'history_question', emotion: 'pain', reply: 'بينزل على إيدي الشمال، وحاسس فكّي مشدود كمان.' }); ai.ledger = { record() { }, guard() { } };
  const eng = new LiveEngine({ ai, scenarios }); const sess = newSession(eng, 'ar');
  eng.noteCharacterLine(sess, 'الوجع لسا موجود، تقيل بنص صدري، بينزل على إيدي الشمال، وحاسس فكّي مشدود كمان.');
  eng.understandingFor = () => ({ understand: async () => ({ data: { spokenLanguage: 'arabic', heard: 'الوجع لسا موجود تقيل بنص صدري بينزل على إيدي الشمال', factIds: [], studentAct: 'other', emotion: 'pain', reply: 'x' }, callId: 'c', ms: 5 }), noteSpoken() { } });
  let meta = null;
  await eng.voiceTurn(sess, 'Patient', Buffer.alloc(9600), { language: 'ar', context: ctx() }, { onMeta: m => meta = m, onAudio() { } });
  assert.equal(meta.noSpeech, true, 'echo of the patient line is not treated as the student');
  // a genuine new question with a couple of shared words still goes through
  eng.understandingFor = () => ({ understand: async () => ({ data: { spokenLanguage: 'arabic', heard: 'وهل الوجع بيزيد لما تتحرك أو تتنفس بعمق؟', factIds: [], studentAct: 'history_question', emotion: 'pain', reply: 'بينزل على إيدي الشمال، وحاسس فكّي مشدود كمان.' }, callId: 'c', ms: 5 }), noteSpoken() { } });
  meta = null; await eng.voiceTurn(sess, 'Patient', Buffer.alloc(9600), { language: 'ar', context: ctx() }, { onMeta: m => meta = m, onAudio() { } });
  assert.notEqual(meta.noSpeech, true);
});

test('embodied actions: accepted actions pass through; standing/walking is declined by the authored policy; unclear side asks; results are reported back to the AI', async () => {
  const mk = data => { const ai = fakeAI(data); ai.ledger = { record() { }, guard() { } }; return new LiveEngine({ ai, scenarios }); };
  let eng = mk({ factIds: [], studentAct: 'other', emotion: 'neutral', action: { type: 'raise_arm', side: 'left', instrument: 'none' }, reply: 'حاضر.' });
  let sess = newSession(eng, 'ar');
  let r = await eng.patientTurn(sess, { utterance: 'ارفع إيدك الشمال', language: 'ar', context: ctx() });
  assert.equal(r.json.action.status, 'accepted'); assert.equal(r.json.action.type, 'raise_arm'); assert.equal(r.json.action.side, 'left');
  eng.actionResult(sess, { persona: 'Patient', type: 'raise_arm', side: 'left', status: 'completed' });
  assert.match(eng.actionResult.length >= 0 && (await import('./lib/actions.mjs')).actionContext(sess), /raise_arm \(left\) — completed/);

  eng = mk({ factIds: [], studentAct: 'other', emotion: 'pain', action: { type: 'stand_up', side: 'none', instrument: 'none' }, reply: 'Sure, I will stand up.' });
  sess = newSession(eng, 'en');
  r = await eng.patientTurn(sess, { utterance: 'Can you stand up and walk to the door?', language: 'en', context: ctx() });
  assert.equal(r.json.action.status, 'denied'); assert.equal(r.json.supervisorIntervention, 'I_UNSAFE_MOBILISE');
  assert.doesNotMatch(r.json.text, /stand up/i, 'never claims an action that will not happen'); assert.match(r.json.text, /can't get up/);

  eng = mk({ factIds: [], studentAct: 'other', emotion: 'neutral', action: { type: 'raise_arm', side: 'unspecified', instrument: 'none' }, reply: 'حاضر.' });
  sess = newSession(eng, 'ar');
  r = await eng.patientTurn(sess, { utterance: 'ارفع إيدك', language: 'ar', context: ctx() });
  assert.equal(r.json.action.status, 'clarify'); assert.match(r.json.text, /اليمين ولا الشمال/);

  eng = mk({ intent: 'smalltalk', investigationId: null, escalationRequested: false, diagnosisNamed: false, isQuestion: false, concepts: [], action: { type: 'step_back', side: 'none', instrument: 'none' }, reply: 'أكيد، بتأخر شوي.' });
  sess = newSession(eng, 'ar');
  r = await eng.instructorTurn(sess, { utterance: 'دكتورة ممكن تبعدي شوي عشان أفحص المريض؟', language: 'ar', context: ctx() });
  assert.equal(r.json.action.status, 'accepted'); assert.equal(r.json.action.type, 'step_back');
});

test('embodied actions v0.6: several actions in one sentence, "nearest arm", new doctor actions, unsafe step blocks the sentence', async () => {
  const mk = data => { const ai = fakeAI(data); ai.ledger = { record() { }, guard() { } }; return new LiveEngine({ ai, scenarios }); };
  let eng = mk({ factIds: [], studentAct: 'physical_request', emotion: 'neutral', actions: [{ type: 'sit_up', side: 'none', instrument: 'none' }, { type: 'raise_arm', side: 'left', instrument: 'none' }], reply: 'حاضر.' });
  let sess = newSession(eng, 'ar');
  let r = await eng.patientTurn(sess, { utterance: 'اقعد شوي وارفع إيدك الشمال', language: 'ar', context: ctx() });
  assert.deepEqual(r.json.actions.map(a => a.type + ':' + a.status), ['sit_up:accepted', 'raise_arm:accepted']);
  eng = mk({ factIds: [], studentAct: 'physical_request', emotion: 'neutral', actions: [{ type: 'extend_arm', side: 'unspecified', instrument: 'none' }], reply: 'تفضل.' });
  r = await eng.patientTurn(newSession(eng, 'ar'), { utterance: 'قرب إيدك علي شوي', language: 'ar', context: ctx() });
  assert.equal(r.json.actions[0].side, 'nearest'); assert.equal(r.json.actions[0].status, 'accepted');
  eng = mk({ factIds: [], studentAct: 'physical_request', emotion: 'pain', actions: [{ type: 'sit_up', side: 'none', instrument: 'none' }, { type: 'walk', side: 'none', instrument: 'none' }], reply: 'حاضر.' });
  r = await eng.patientTurn(newSession(eng, 'ar'), { utterance: 'اقعد وبعدين امشي معي', language: 'ar', context: ctx() });
  assert.equal(r.json.actions.length, 1); assert.equal(r.json.actions[0].status, 'denied', 'an unsafe step declines the request instead of half-doing it');
  for (const [said, type] of [['تعالي من الجهة الثانية', 'go_other_side'], ['ممكن تتركيني أفحص المريض لحالي؟', 'observe_from_distance']]) {
    eng = mk({ intent: 'physical_request', investigationId: null, escalationRequested: false, diagnosisNamed: false, isQuestion: false, concepts: [], actions: [{ type: 'other_physical', side: 'none', instrument: 'none' }], reply: 'أكيد.' });
    r = await eng.instructorTurn(newSession(eng, 'ar'), { utterance: said, language: 'ar', context: ctx() });
    assert.equal(r.json.actions[0].type, type, said);
  }
  // a symptom question never becomes an action through the safety net
  eng = mk({ factIds: ['H_TIMING'], studentAct: 'history_question', emotion: 'pain', actions: [], reply: 'ما راح أبداً.' });
  r = await eng.patientTurn(newSession(eng, 'ar'), { utterance: 'رجع الوجع لما ارتحت؟', language: 'ar', context: ctx() });
  assert.equal(r.json.actions.length, 0);
});

test('embodied actions v0.6: an unambiguous arm verb corrects a mislabelled arm action, and the reply never describes the other movement', async () => {
  const mk = data => { const ai = fakeAI(data); ai.ledger = { record() { }, guard() { } }; return new LiveEngine({ ai, scenarios }); };
  let eng = mk({ factIds: [], studentAct: 'physical_request', emotion: 'neutral', actions: [{ type: 'extend_arm', side: 'left', instrument: 'none' }], reply: 'طيب، رح أمد إيدي الشمال.' });
  let r = await eng.patientTurn(newSession(eng, 'ar'), { utterance: 'ارفع إيدك شمال لو سمحت', language: 'ar', context: ctx() });
  assert.equal(r.json.actions[0].type, 'raise_arm'); assert.equal(r.json.actions[0].side, 'left'); assert.equal(r.json.text, 'حاضر.');
  eng = mk({ factIds: [], studentAct: 'physical_request', emotion: 'neutral', actions: [{ type: 'extend_arm', side: 'right', instrument: 'none' }], reply: 'تفضل.' });
  r = await eng.patientTurn(newSession(eng, 'ar'), { utterance: 'مد إيدك اليمين', language: 'ar', context: ctx() });
  assert.equal(r.json.actions[0].type, 'extend_arm'); assert.equal(r.json.text, 'تفضل.', 'a correct action keeps its natural reply');
});
