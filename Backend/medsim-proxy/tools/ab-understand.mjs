// A/B of the Realtime understanding model on Arabic turns (Arabic locked). Billed (small). Usage: node tools/ab-understand.mjs <label>
import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url)); const BASE = 'http://127.0.0.1:8787', H = { 'X-MedSim-Client': loadSecrets().clientToken };
const AUDIO = path.resolve(HERE, '../data/student-audio');
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const sid = (await (await fetch(BASE + '/v1/session/start', { method: 'POST', headers: { ...H, 'Content-Type': 'application/json' }, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: 'ar' }) })).json()).sessionId;
await new Promise(r => setTimeout(r, 1500));
const out = [];
for (const [persona, clip] of [['Instructor', 'student_en_dr_hello'], ['Instructor', 'student_ar_dr_ecg'], ['Instructor', 'student_en_dr_mi'], ['Patient', 'student_ar_reassure']]) {
  const q = new URLSearchParams({ sessionId: sid, persona, lang: 'ar', langLock: 'ar', ctx: Buffer.from(JSON.stringify({ stage: 'HistoryTaking', stateId: 'S_PRESENTATION', events: ['FACT:H_ONSET'] })).toString('base64url') });
  const t0 = Date.now(); const r = await fetch(`${BASE}/v1/voice?${q}`, { method: 'POST', headers: { ...H, 'Content-Type': 'application/octet-stream' }, body: fs.readFileSync(path.join(AUDIO, clip + '.wav')).subarray(44) });
  const buf = Buffer.from(await r.arrayBuffer()); const m = JSON.parse(buf.subarray(0, buf.indexOf(10)).toString('utf8'));
  const line = { model: process.argv[2], persona, heard: m.heard, reply: m.text, source: m.source, understandMs: m.timings?.understandMs, totalMs: Date.now() - t0 };
  out.push(line); console.log(JSON.stringify(line));
}
fs.writeFileSync(path.resolve(HERE, `../data/ab-${process.argv[2]}.json`), JSON.stringify(out, null, 2));
