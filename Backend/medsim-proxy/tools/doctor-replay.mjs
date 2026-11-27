// Replays doctor-directed student clips through the native voice path and prints what was heard, the reply,
// whether grounding accepted it (and why not), and latency. Billed (small). Usage: node tools/doctor-replay.mjs [stage]
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const BASE = 'http://127.0.0.1:8787', TOKEN = loadSecrets().clientToken;
const AUDIO = path.resolve(HERE, '../data/student-audio');
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const stage = process.argv[2] || 'HistoryTaking';
const clips = (process.argv[3] || 'student_en_dr_hello,student_en_dr_ecg,student_en_dr_next,student_en_dr_hint,student_en_dr_mi').split(',');
const r0 = await fetch(BASE + '/v1/session/start', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MedSim-Client': TOKEN }, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: 'en' }) });
const sid = (await r0.json()).sessionId;
await new Promise(r => setTimeout(r, 1500)); // let prewarm finish (as in the app: the session starts during the welcome)
for (const c of clips) {
  const q = new URLSearchParams({ sessionId: sid, persona: 'Instructor', lang: 'en', ctx: Buffer.from(JSON.stringify({ stage, stateId: 'S_PRESENTATION', events: [] })).toString('base64url') });
  const t0 = Date.now();
  const r = await fetch(`${BASE}/v1/voice?${q}`, { method: 'POST', headers: { 'X-MedSim-Client': TOKEN, 'Content-Type': 'application/octet-stream' }, body: fs.readFileSync(path.join(AUDIO, c + '.wav')).subarray(44) });
  const buf = Buffer.from(await r.arrayBuffer()); const nl = buf.indexOf(10);
  const m = JSON.parse(buf.subarray(0, nl).toString('utf8'));
  console.log(JSON.stringify({ clip: c, heard: m.heard, reply: m.text, source: m.source, rejected: m.rejected, intent: m.intent, totalMs: Date.now() - t0 }));
}
await fetch(BASE + '/v1/session/end', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MedSim-Client': TOKEN }, body: JSON.stringify({ sessionId: sid }) });
