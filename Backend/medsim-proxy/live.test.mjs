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
