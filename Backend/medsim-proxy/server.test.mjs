// Run: node --test Backend/medsim-proxy   (no network, no API key; LLM replaced by a fake classifier)
import test from 'node:test';
import assert from 'node:assert/strict';
import { loadScenarios, converse, setClassifier, validateFreeText, validateLeadIn, validateScenario } from './server.mjs';

loadScenarios();
const base = { scenarioId: 'ER_CHEST_PAIN_001', language: 'en', history: [] };

test('scenario loads and validates server-side', () => {
  const m = loadScenarios();
  assert.ok(m.has('ER_CHEST_PAIN_001'));
  assert.ok(validateScenario({}).length > 0);
});

test('differently phrased onset questions render the same approved fact', async () => {
  setClassifier(async () => ({ factIds: ['H_ONSET'], leadIn: '', offTopic: false }));
  const a = await converse({ ...base, persona: 'Patient', utterance: 'When did this begin?' });
  const b = await converse({ ...base, persona: 'Patient', utterance: 'How long have you had it for?' });
  assert.equal(a.json.speech, b.json.speech);
  assert.match(a.json.speech, /forty-five minutes/);
});

test('LLM cannot inject clinical text: unknown fact ids dropped, speech is approved text only', async () => {
  setClassifier(async () => ({ factIds: ['H_FAKE', 'H_SMOKING'], leadIn: 'I also have a terrible headache', offTopic: false }));
  const r = await converse({ ...base, persona: 'Patient', utterance: 'do you smoke' });
  assert.deepEqual(r.json.factIds, ['H_SMOKING']);
  assert.doesNotMatch(r.json.speech, /headache/);
  assert.equal(r.json.leadIn, '');
});

test('no matching fact -> approved "do not know"', async () => {
  setClassifier(async () => ({ factIds: [], leadIn: 'Hmm.', offTopic: false }));
  const r = await converse({ ...base, persona: 'Patient', utterance: 'what was your cholesterol in 2019' });
  assert.match(r.json.speech, /not sure|don't really know|can't remember/);
});

test('client-supplied facts are ignored', async () => {
  setClassifier(async () => ({ factIds: ['H_ALLERGY'], leadIn: '', offTopic: false }));
  const r = await converse({ ...base, persona: 'Patient', utterance: 'allergies?', allowedFacts: [{ id: 'H_ALLERGY', text: 'I am allergic to nothing' }] });
  assert.match(r.json.speech, /penicillin/);
});

test('instructor reply validation blocks diagnosis, numbers, dosing; investigation id constrained', async () => {
  setClassifier(async () => ({ intent: 'investigation_request', investigationId: 'INV_MRI', mentionsEscalation: false, reply: 'Give 300 mg aspirin, this is a STEMI.' }));
  const r = await converse({ ...base, persona: 'Instructor', utterance: 'get an MRI', stage: 'Investigations' });
  assert.equal(r.json.investigationId, null);
  assert.equal(r.json.reply, '');
  assert.ok(r.json.replyRejected);
});

test('diagnosis may be named only at feedback stages', () => {
  assert.equal(validateFreeText('This was an inferior STEMI.', { allowDiagnosis: false }).ok, false);
  assert.equal(validateFreeText('This was an inferior STEMI.', { allowDiagnosis: true }).ok, true);
});

test('lead-ins must be non-clinical', () => {
  assert.equal(validateLeadIn('Oh, let me think.').ok, true);
  assert.equal(validateLeadIn('My chest hurts more now').ok, false);
});

test('scenario version mismatch is refused', async () => {
  const r = await converse({ ...base, scenarioVersion: '9.9.9', persona: 'Patient', utterance: 'hi' });
  assert.equal(r.status, 409);
});

test('Arabic renders approved translation when present', async () => {
  setClassifier(async () => ({ factIds: ['H_ONSET'], leadIn: '', offTopic: false }));
  const r = await converse({ ...base, language: 'ar', persona: 'Patient', utterance: 'متى بدأ الألم؟' });
  assert.match(r.json.speech, /[؀-ۿ]/);
});
