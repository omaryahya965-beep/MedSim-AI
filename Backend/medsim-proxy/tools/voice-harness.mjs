// Live test of the native voice path (POST /v1/voice): student AUDIO in → validated reply → streamed speech out.
// Measures end-of-student-speech → first audio byte (the student's perceived response time). Billed (small).
// Usage: node tools/voice-harness.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
import { pcmToWav } from '../lib/openai.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const BASE = 'http://127.0.0.1:8787', TOKEN = loadSecrets().clientToken;
const AUDIO = path.resolve(HERE, '../data/student-audio'), OUT = path.resolve(HERE, '../data/voice-out'); fs.mkdirSync(OUT, { recursive: true });
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const pcmOf = f => fs.readFileSync(path.join(AUDIO, f)).subarray(44);
async function session(lang) { const r = await fetch(BASE + '/v1/session/start', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-MedSim-Client': TOKEN }, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: lang }) }); return (await r.json()).sessionId; }

async function voice(sid, persona, file, ctx, { lang = 'en', abortAfterMs = 0, name } = {}) {
  const ac = new AbortController();
  const q = new URLSearchParams({ sessionId: sid, persona, lang, ctx: Buffer.from(JSON.stringify(ctx)).toString('base64url') });
  const t0 = Date.now();
  const r = await fetch(`${BASE}/v1/voice?${q}`, { method: 'POST', headers: { 'X-MedSim-Client': TOKEN, 'Content-Type': 'application/octet-stream' }, body: pcmOf(file), signal: ac.signal });
  if (!r.ok) return { name, status: r.status, error: await r.text() };
  const reader = r.body.getReader(); let buf = Buffer.alloc(0), meta = null, tMeta = 0, tFirst = 0; const audio = [];
  try {
    while (true) {
      const { value, done } = await reader.read(); if (done) break;
      let chunk = Buffer.from(value);
      if (!meta) { buf = Buffer.concat([buf, chunk]); const nl = buf.indexOf(10); if (nl < 0) continue; meta = JSON.parse(buf.subarray(0, nl).toString('utf8')); tMeta = Date.now() - t0; chunk = buf.subarray(nl + 1); }
      if (chunk.length) { if (!tFirst) tFirst = Date.now() - t0; audio.push(chunk); }
      if (abortAfterMs && tFirst && Date.now() - t0 > tFirst + abortAfterMs) { ac.abort(); break; }
    }
  } catch (e) { if (e.name !== 'AbortError') throw e; }
  const pcm = Buffer.concat(audio);
  if (name) fs.writeFileSync(path.join(OUT, name + '.wav'), pcmToWav(pcm));
  return { name, heard: meta?.heard, reply: meta?.text, source: meta?.source, rejected: meta?.rejected, factIds: meta?.factIds, intent: meta?.intent, esc: meta?.escalationRequested, dxQ: meta?.isQuestion, understandMs: meta?.timings?.understandMs, metaMs: tMeta, firstAudioMs: tFirst, totalMs: Date.now() - t0, audioSec: +(pcm.length / 48000).toFixed(2), interrupted: !!abortAfterMs };
}

const ctx = (o = {}) => ({ stage: 'HistoryTaking', stateId: 'S_PRESENTATION', events: [], ...o });
const results = [];
const sEn = await session('en');
results.push(await voice(sEn, 'Patient', 'student_en_intro.wav', ctx(), { name: 'patient_en_intro' }));
results.push(await voice(sEn, 'Patient', 'student_en_allergy.wav', ctx({ events: ['FACT:H_ONSET'] }), { name: 'patient_en_allergy' }));
results.push(await voice(sEn, 'Patient', 'student_en_allergy.wav', ctx({ events: ['FACT:H_ONSET', 'FACT:H_ALLERGY'] }), { name: 'patient_en_interrupted', abortAfterMs: 600 }));
results.push(await voice(sEn, 'Instructor', 'student_en_stemi_q.wav', ctx({ stage: 'Investigations', events: ['INV:INV_ECG', 'ECG_REVIEWED'] }), { name: 'doctor_en_stemi_question' }));
results.push(await voice(sEn, 'Instructor', 'student_en_escalate.wav', ctx({ stage: 'Investigations', events: ['INV:INV_ECG', 'ECG_REVIEWED'] }), { name: 'doctor_en_escalation' }));
const sAr = await session('ar');
results.push(await voice(sAr, 'Patient', 'student_ar_onset.wav', ctx(), { lang: 'ar', name: 'patient_ar_onset' }));
results.push(await voice(sAr, 'Patient', 'student_en_allergy.wav', ctx({ stateId: 'S_DETERIORATION' }), { lang: 'ar', name: 'patient_ar_state_deterioration' }));
for (const r of results) console.log(JSON.stringify(r));
fs.writeFileSync(path.resolve(HERE, '../data/voice-results.json'), JSON.stringify(results, null, 2));
const u = await (await fetch(BASE + '/v1/usage', { headers: { 'X-MedSim-Client': TOKEN } })).json();
console.log(`spent so far: $${u.spentUsd} of $${u.budgetUsd}`);
