// Natural-language → validated physical action, real API (typed path; same prompts/schema as voice). Billed (tiny).
// Usage: node tools/action-harness.mjs
import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url)); const BASE = 'http://127.0.0.1:8787', H = { 'X-MedSim-Client': loadSecrets().clientToken, 'Content-Type': 'application/json' };
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const sid = (await (await fetch(BASE + '/v1/session/start', { method: 'POST', headers: H, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: 'ar' }) })).json()).sessionId;
const cases = [
  ['Patient', 'عمو، ممكن تقعد شوي؟', 'sit_up'], ['Patient', 'ارفع إيدك الشمال.', 'raise_arm:left'], ['Patient', 'مد إيدك اليمين.', 'extend_arm:right'],
  ['Patient', 'لف شوي على جنبك الشمال.', 'turn_to_side:left'], ['Patient', 'خذ نفس عميق.', 'deep_breath'], ['Patient', 'افتح ثمك.', 'open_mouth'],
  ['Patient', 'طلع فيي شوي.', 'look_at_student'], ['Patient', 'فرجيني وين الوجع.', 'point_to_pain'], ['Patient', 'خليك ثابت شوي، بدي أفحصك.', 'hold_still'],
  ['Patient', 'ممكن تقرب إيدك علي؟', 'extend_arm|clarify'], ['Patient', 'ارفع إيدك.', 'raise_arm|clarify'], ['Patient', 'قوم امشي معي للباب.', 'stand_up|walk:denied'],
  ['Patient', 'Could you sit up a little for me, please?', 'sit_up'], ['Patient', 'Lift your right arm for me.', 'raise_arm:right'], ['Patient', 'وين بالزبط الوجع؟', 'none|point_to_pain'],
  ['Patient', 'Is the pain worse when you breathe in deeply?', 'none'],
  ['Instructor', 'دكتورة تعالي هون.', 'come_to_student'], ['Instructor', 'ممكن تبعدي شوي عشان أفحص المريض؟', 'step_back'], ['Instructor', 'وقفي جنبي.', 'stand_beside_student'],
  ['Instructor', 'تعالي شوفي المريض.', 'approach_patient'], ['Instructor', 'استني شوي.', 'wait'], ['Instructor', 'فرجيني كيف أستخدم جهاز الضغط.', 'demonstrate_instrument:bp_cuff'],
  ['Instructor', 'Could you step back a bit so I can examine him?', 'step_back'],
];
let ok = 0; const out = [];
for (const [persona, utter, expect] of cases) {
  const t0 = Date.now();
  const r = await (await fetch(BASE + '/v1/turn', { method: 'POST', headers: H, body: JSON.stringify({ sessionId: sid, persona, utterance: utter, language: 'ar', speak: false, context: { stage: 'PhysicalExamination', stateId: 'S_PRESENTATION', events: ['FACT:H_CONSENT'] } }) })).json();
  const a = r.action || { type: 'none', side: 'none', status: 'none' };
  const got = a.type + (a.side && a.side !== 'none' && a.side !== 'unspecified' ? ':' + a.side : '') + (a.instrument && a.instrument !== 'none' ? ':' + a.instrument : '');
  const pass = expect.split('|').some(e => { const [want, st] = e.split(':').length === 2 && ['denied', 'clarify'].includes(e.split(':')[1]) ? [e.split(':')[0], e.split(':')[1]] : [e, null];
    if (e === 'clarify') return a.status === 'clarify'; if (e.endsWith(':denied')) return a.status === 'denied' && e.split(':')[0].split('|').includes(a.type) || (a.status === 'denied');
    return got === e || (e === 'none' && a.type === 'none'); });
  if (pass) ok++;
  const line = { persona, said: utter, expect, got, status: a.status, reply: r.text, source: r.source, pass, ms: Date.now() - t0 };
  out.push(line); console.log(JSON.stringify(line));
}
console.log(`\n${ok}/${cases.length} instructions mapped as expected`);
fs.writeFileSync(path.resolve(HERE, '../data/action-results.json'), JSON.stringify(out, null, 2));
