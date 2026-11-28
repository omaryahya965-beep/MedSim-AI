// Surgical module tests. No network, no API key, no spending: the AI client is a fake.
// Run: node --test Backend/medsim-proxy
import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
process.env.MEDSIM_CLIENT_TOKEN = process.env.MEDSIM_CLIENT_TOKEN || 'test-token';
process.env.MEDSIM_SECRETS_FILE = '/nonexistent';
const { loadSurgery, surgeryScenarios, loadScenarios, createServer } = await import('./server.mjs');
const { SurgeryEngine, checkSurgicalGrounding, sanitizeSurgeryContext, validateSurgicalScenario, scenarioCorpus } = await import('./lib/surgery.mjs');

loadSurgery(); const scenarios = loadScenarios();
const S = surgeryScenarios.get('OR_TKA_TRAINING_001').data;

function fakeAI(next) {
  const calls = [];
  return { calls, configured: true, apiKey: 'x', ledger: { record() { }, guard() { } },
    async json(args) { calls.push(args); return { data: typeof next === 'function' ? next(args) : next }; },
    async speak(text, { onAudio }) { onAudio?.(Buffer.alloc(480)); return { pcm: Buffer.alloc(480), transcript: text, firstAudioMs: 5, ms: 10 }; } };
}
const intent = (o = {}) => ({ addressedTo: 'surgeon', action: 'none', instrumentId: 'none', forCurrentObjective: false, help: false, explain: false, languageCommand: 'none', ...o });
function engine(next) { const ai = fakeAI(next); const e = new SurgeryEngine({ ai, scenarios: surgeryScenarios }); const s = e.get(e.start({ scenarioId: S.scenarioId, language: 'en' }).json.sessionId); return { e, s, ai }; }

test('surgical scenario loads and validates; modules stay independent', () => {
  assert.deepEqual(validateSurgicalScenario(S), []);
  assert.ok(!surgeryScenarios.has('ER_CHEST_PAIN_001'));
  assert.ok(!scenarios.has('OR_TKA_TRAINING_001'));
  assert.equal(S.review.status, 'DRAFT');
});

test('unknown scenario and version mismatch are refused', () => {
  const e = new SurgeryEngine({ ai: fakeAI({}), scenarios: surgeryScenarios });
  assert.equal(e.start({ scenarioId: 'ER_CHEST_PAIN_001' }).status, 404);
  assert.equal(e.start({ scenarioId: S.scenarioId, scenarioVersion: '9.9.9' }).status, 409);
});

test('help request is deferred: proxy does not voice it, Unity delivers the approved hint', async () => {
  const { e, s } = engine({ intent: intent({ help: true }), reply: 'Sure, start by cutting the bone with the saw.' });
  const r = await e.turn(s, { characterId: 'surgeon', utterance: 'دكتور، ممكن تساعدني؟', context: { objectiveId: 'obj.site_confirmation' } });
  assert.equal(r.json.deferred, true); assert.equal(r.json.text, ''); assert.equal(r.json.intent.help, true);
});

test('instrument requests are routed to the assistant and ids are constrained', async () => {
  const { e, s } = engine({ intent: intent({ addressedTo: 'surgeon', action: 'hand_instrument', instrumentId: 'inst.scalpel' }), reply: 'Here it is, done.' });
  const r = await e.turn(s, { characterId: 'surgeon', utterance: 'دكتور ممكن تعطيني المشرط', context: {} });
  assert.equal(r.json.intent.characterId, 'assistant'); assert.equal(r.json.intent.instrumentId, 'inst.scalpel');
  assert.equal(r.json.deferred, true); assert.equal(r.json.text, '', 'never voices "done" before the simulation confirms');
  const { e: e2, s: s2 } = engine({ intent: intent({ action: 'teleport', instrumentId: 'inst.lightsaber' }), reply: 'ok' });
  const r2 = await e2.turn(s2, { characterId: 'assistant', utterance: 'teleport', context: {} });
  assert.equal(r2.json.intent.action, 'other_physical'); assert.equal(r2.json.intent.instrumentId, '');
});

test('ungrounded surgical content is replaced with approved text', async () => {
  const { e, s } = engine({ intent: intent(), reply: 'Be careful of the popliteal artery behind the knee.' });
  const r = await e.turn(s, { characterId: 'surgeon', utterance: 'why do we expose the joint?', context: { objectiveId: 'obj.exposure' } });
  assert.equal(r.json.source, 'approved-fallback'); assert.match(r.json.rejected, /artery/);
  assert.equal(r.json.text, S.objectives.find(o => o.id === 'obj.exposure').explanation.en);
});

test('grounded conversational reply passes', async () => {
  const { e, s } = engine({ intent: intent(), reply: 'We hold the tissues apart so the joint surfaces can be seen.' });
  const r = await e.turn(s, { characterId: 'surgeon', utterance: 'why the retractor?', context: { objectiveId: 'obj.exposure' } });
  assert.equal(r.json.source, 'ai'); assert.equal(r.json.deferred, false);
});

test('typed language command switches without calling the model', async () => {
  const { e, s, ai } = engine({ intent: intent(), reply: 'x' });
  const r = await e.turn(s, { characterId: 'assistant', utterance: 'احكي عربي', context: {} });
  assert.equal(r.json.lang, 'ar'); assert.equal(r.json.source, 'language-command'); assert.equal(ai.calls.length, 0);
});

test('say: base text must be grounded in the scenario; approvedOnly is verbatim; bad rephrase falls back', async () => {
  const { e, s } = engine({ line: 'Use the scalpel 12 times along the line.' });
  assert.equal((await e.say(s, { characterId: 'surgeon', baseText: 'Cut the femoral artery now.' })).status, 422);
  const base = S.supervisorRules.find(r => r.id === 'rule.wrong_side').line.en;
  const v = await e.say(s, { characterId: 'surgeon', baseText: base, approvedOnly: true });
  assert.equal(v.json.text, base); assert.equal(v.json.source, 'approved');
  const hint = S.objectives[1].hints[1].en;
  const p = await e.say(s, { characterId: 'surgeon', baseText: hint });
  assert.equal(p.json.text, hint, 'rephrase that adds a number is rejected'); assert.ok(p.json.rejected);
});

test('debrief rephrase may only use the facts in the base text', async () => {
  const base = 'We\'re done. You completed 2 of 9 steps and used 1 hints. Confirmed the operative site cleanly before starting. Remember, this is an educational simulation that is not clinically validated.';
  const { e, s } = engine({ line: 'Great work: you completed 2 of 9 steps with 1 hint, and you confirmed the operative site cleanly. This is an educational simulation, not clinically validated.' });
  const facts = { completed: ['obj.site_confirmation', 'obj.incision'], missed: new Array(7).fill('x'), hints: 1, interventions: 0, score: 5, maxScore: 26, durationSeconds: 300 };
  assert.equal((await e.say(s, { characterId: 'surgeon', kind: 'debrief', baseText: base })).status, 422, 'numbers need the facts payload');
  const r = await e.say(s, { characterId: 'surgeon', kind: 'debrief', baseText: base, facts });
  assert.equal(r.json.source, 'ai');
  const { e: e2, s: s2 } = engine({ line: 'You completed 7 of 9 steps.' });
  assert.equal((await e2.say(s2, { characterId: 'surgeon', kind: 'debrief', baseText: base, facts })).json.text, base);
});

test('context from Unity is ids only; unknown ids dropped', () => {
  const c = sanitizeSurgeryContext(S, { objectiveId: 'obj.incision', completed: ['obj.site_confirmation', 'FACT:H_ONSET', 'obj.fake'], holding: 'inst.scalpel', recent: ['tool.grabbed:inst.scalpel', 'DROP TABLE'] });
  assert.equal(c.objectiveId, 'obj.incision'); assert.deepEqual(c.completed, ['obj.site_confirmation']); assert.deepEqual(c.recent, ['tool.grabbed:inst.scalpel']);
});

test('surgical grounding blocks meta talk and dosing', () => {
  const corpus = scenarioCorpus(S);
  assert.equal(checkSurgicalGrounding('As an AI, I think the scalpel is next.', { corpus }).reason, 'meta_reference');
  assert.equal(checkSurgicalGrounding('Give 2 g of cefazolin first.', { corpus }).ok, false);
  assert.equal(checkSurgicalGrounding('Take the scalpel and follow the highlighted line.', { corpus }).ok, true);
});

test('HTTP: surgical routes need the client token and are isolated from clinical sessions', async () => {
  const srv = createServer(); await new Promise(r => srv.listen(0, '127.0.0.1', r)); const port = srv.address().port;
  const call = (path, body, token = process.env.MEDSIM_CLIENT_TOKEN) => new Promise((resolve, reject) => {
    const data = Buffer.from(JSON.stringify(body));
    const req = http.request({ host: '127.0.0.1', port, path, method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MedSim-Client': token, 'Content-Length': data.length } }, res => { let b = ''; res.on('data', d => b += d); res.on('end', () => resolve({ status: res.statusCode, json: JSON.parse(b || '{}') })); });
    req.on('error', reject); req.end(data);
  });
  try {
    assert.equal((await call('/v1/surgery/session/start', { scenarioId: S.scenarioId }, 'wrong')).status, 401);
    const st = await call('/v1/surgery/session/start', { scenarioId: S.scenarioId, language: 'ar' });
    assert.equal(st.status, 200); assert.equal(st.json.reviewStatus, 'DRAFT');
    assert.equal((await call('/v1/session/start', { scenarioId: S.scenarioId })).status, 404, 'clinical endpoint does not know surgical scenarios');
    assert.equal((await call('/v1/surgery/session/start', { scenarioId: 'ER_CHEST_PAIN_001' })).status, 404, 'surgical endpoint does not know the chest-pain scenario');
    assert.equal((await call('/v1/surgery/turn', { sessionId: 'nope', utterance: 'hi' })).status, 404);
    assert.equal((await call('/v1/surgery/event', { sessionId: st.json.sessionId, context: { objectiveId: 'obj.incision' } })).json.context.objectiveId, 'obj.incision');
    assert.equal((await call('/v1/surgery/session/end', { sessionId: st.json.sessionId })).status, 200);
  } finally { srv.close(); }
});

test('demonstration requests are catalogue actions for both characters (the student sees it done, then tries)', async () => {
  for (const who of ['surgeon', 'assistant']) for (const action of ['demonstrate_step', 'demonstrate_procedure']) {
    const { e, s } = engine({ intent: intent({ addressedTo: who, action }), reply: 'Sure, watch me.' });
    const r = await e.turn(s, { characterId: who, utterance: action === 'demonstrate_step' ? 'ورجيني كيف أعلم على الركبة' : 'show me the whole procedure', context: { objectiveId: 'obj.site_confirmation' } });
    assert.equal(r.json.intent.action, action, who + ' ' + action);
    assert.equal(r.json.intent.characterId, who, 'the addressed character demonstrates');
  }
});

test('Arabic medical spelling is corrected in spoken replies («راكبة» → «ركبة»)', async () => {
  const { fixArabicTerms } = await import('./lib/live.mjs');
  assert.equal(fixArabicTerms('رح نعلّم على الراكبة اليمنى'), 'رح نعلّم على الركبة اليمنى');
  assert.equal(fixArabicTerms('الركبه اليمنى وبالراكبة'), 'الركبة اليمنى وبالركبة');
  assert.equal(fixArabicTerms('the knee stays'), 'the knee stays');
  assert.equal(fixArabicTerms('مركبة'), 'مركبة', 'other words untouched');
  const { e, s } = engine({ intent: intent(), reply: 'هاي الراكبة اليمنى.' });
  const r = await e.turn(s, { characterId: 'surgeon', utterance: 'أي ركبة؟', context: { objectiveId: 'obj.site_confirmation' } });
  assert.ok(!r.json.text.includes('راكبة'), r.json.text);
});

test('a line repeated in the same visit is re-worded, not replayed word for word', async () => {
  const { e, s, ai } = engine({ line: 'Start by confirming the site.' });
  const base = 'Start by confirming which knee we are operating on.';
  await e.say(s, { characterId: 'surgeon', kind: 'hint', baseText: base, language: 'en' });
  await e.say(s, { characterId: 'surgeon', kind: 'hint', baseText: base, language: 'en' });
  const sys = ai.calls.map(c => c.system);
  assert.ok(!/already said/.test(sys[0]), 'first time: plain');
  assert.ok(/already said this once/.test(sys[1]), 'second time: told to vary the wording');
});
