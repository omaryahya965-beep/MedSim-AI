// v0.6 natural-language → validated action LIST (multi-step, nearest arm, new doctor moves, MSA/English/mixed). Real API, typed path. Billed (tiny).
// Usage: node tools/action-harness-v06.mjs
import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url)); const BASE = 'http://127.0.0.1:8787', H = { 'X-MedSim-Client': loadSecrets().clientToken, 'Content-Type': 'application/json' };
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const sid = (await (await fetch(BASE + '/v1/session/start', { method: 'POST', headers: H, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: 'ar' }) })).json()).sessionId;
// expected = action list "type[:side]" joined by "+", alternatives by "|"; "denied"/"clarify" match on status
const cases = [
  ['Patient', 'اقعد شوي وارفع إيدك الشمال.', 'sit_up+raise_arm:left'],
  ['Patient', 'قرب إيدك علي.', 'extend_arm:nearest'],
  ['Patient', 'ريّح إيدك.', 'lower_arm|lower_arm:both|lower_arm:left|lower_arm:right|clarify'],
  ['Patient', 'سكّر ثمك هلأ.', 'close_mouth'],
  ['Patient', 'ارجع تمدد.', 'recline'],
  ['Patient', 'تنفس عادي.', 'breathe_normally'],
  ['Patient', 'تحرك شوي لفوق عالتخت.', 'shift_on_bed'],
  ['Patient', 'من فضلك، اجلس قليلاً ثم خذ نفساً عميقاً.', 'sit_up+deep_breath'],
  ['Patient', 'ارفع يدك اليمنى من فضلك.', 'raise_arm:right'],
  ['Patient', 'Sit up please, then open your mouth.', 'sit_up+open_mouth'],
  ['Patient', 'ممكن ترفع your left arm؟', 'raise_arm:left'],
  ['Patient', 'اقعد وبعدين قوم امشي.', 'denied'],
  ['Patient', 'رجع الوجع لما مشيت؟', 'none'],
  ['Instructor', 'تعالي من الجهة الثانية.', 'go_other_side'],
  ['Instructor', 'ممكن تتركيني أفحص المريض لحالي؟', 'observe_from_distance|step_back'],
  ['Instructor', 'Could you come to the other side of the bed?', 'go_other_side'],
  ['Instructor', 'دكتورة شو التشخيص برأيك؟', 'none'],
];
let ok = 0; const out = [];
for (const [persona, utter, expect] of cases) {
  const t0 = Date.now();
  const r = await (await fetch(BASE + '/v1/turn', { method: 'POST', headers: H, body: JSON.stringify({ sessionId: sid, persona, utterance: utter, language: 'ar', speak: false, context: { stage: 'PhysicalExamination', stateId: 'S_PRESENTATION', events: ['FACT:H_CONSENT'] } }) })).json();
  const list = (r.actions && r.actions.length ? r.actions : (r.action ? [r.action] : [])).filter(a => a && a.type !== 'none');
  const got = list.map(a => a.type + (a.side && !['none', 'unspecified'].includes(a.side) ? ':' + a.side : '')).join('+') || 'none';
  const statuses = list.map(a => a.status);
  const pass = expect.split('|').some(e => e === 'denied' ? statuses.includes('denied') : e === 'clarify' ? statuses.includes('clarify') : got === e && !statuses.some(s => s === 'denied'));
  if (pass) ok++;
  const line = { persona, said: utter, expect, got, statuses: statuses.join(','), reply: r.text, intent: r.intent || r.act, pass, ms: Date.now() - t0 };
  out.push(line); console.log(JSON.stringify(line));
}
console.log(`\n${ok}/${cases.length} instructions mapped as expected`);
fs.writeFileSync(path.resolve(HERE, '../data/action-results-v06.json'), JSON.stringify(out, null, 2));
