// v0.6: spoken physical instruction + a plain history question through the real /v1/voice path (Realtime understanding). Billed (small).
// Usage: node tools/voice-action-check.mjs
import fs from 'node:fs'; import path from 'node:path'; import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url)); const BASE = 'http://127.0.0.1:8787', TOKEN = loadSecrets().clientToken;
const AUDIO = path.resolve(HERE, '../data/student-audio');
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const sid = (await (await fetch(BASE + '/v1/session/start', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MedSim-Client': TOKEN }, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: 'ar' }) })).json()).sessionId;
const ctx = { stage: 'PhysicalExamination', stateId: 'S_PRESENTATION', events: ['FACT:H_CONSENT'] };
for (const [file, lang, expect] of [['student_ar_act_raise.wav', 'ar', 'raise_arm:left'], ['student_en_allergy.wav', 'en', 'none'], ['student_ar_act_sit.wav', 'ar', 'sit_up']]) {
  const q = new URLSearchParams({ sessionId: sid, persona: 'Patient', lang, ctx: Buffer.from(JSON.stringify(ctx)).toString('base64url') });
  const t0 = Date.now(); const r = await fetch(`${BASE}/v1/voice?${q}`, { method: 'POST', headers: { 'X-MedSim-Client': TOKEN, 'Content-Type': 'application/octet-stream' }, body: fs.readFileSync(path.join(AUDIO, file)).subarray(44) });
  const reader = r.body.getReader(); let buf = Buffer.alloc(0), meta = null, first = 0;
  while (true) { const { value, done } = await reader.read(); if (done) break; if (!meta) { buf = Buffer.concat([buf, Buffer.from(value)]); const nl = buf.indexOf(10); if (nl < 0) continue; meta = JSON.parse(buf.subarray(0, nl).toString('utf8')); if (buf.length > nl + 1 && !first) first = Date.now() - t0; } else if (!first) first = Date.now() - t0; }
  const acts = (meta?.actions || []).map(a => a.type + (a.side && a.side !== 'none' ? ':' + a.side : '') + (a.corrected ? '(corrected)' : '')).join('+') || 'none';
  console.log(JSON.stringify({ file, heard: meta?.heard, actions: acts, pass: acts.replace('(corrected)', '') === expect, reply: meta?.text, source: meta?.source, firstAudioMs: first }));
}
