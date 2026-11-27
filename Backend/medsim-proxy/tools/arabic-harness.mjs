// Live Arabic conversation test through the native voice path, emulating the Unity client's language state
// (Auto → explicit Arabic → explicit English). Student audio = SYNTHETIC clips (automated, not a human voice).
// Checks reply language, grounding source, and that every spoken line's audio arrived complete. Billed (small).
// Usage: node tools/arabic-harness.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadSecrets } from '../lib/secrets.mjs';
import { scriptLang } from '../lib/language.mjs';
import { pcmToWav } from '../lib/openai.mjs';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const BASE = 'http://127.0.0.1:8787', TOKEN = loadSecrets().clientToken;
const AUDIO = path.resolve(HERE, '../data/student-audio'), OUT = path.resolve(HERE, '../data/arabic-out'); fs.mkdirSync(OUT, { recursive: true });
const SCEN = JSON.parse(fs.readFileSync(path.resolve(HERE, '../../../Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json'), 'utf8'));
const H = { 'X-MedSim-Client': TOKEN };
let conv = 'en', lock = '';   // the user's failing case: app language English, nothing chosen explicitly
const sid = (await (await fetch(BASE + '/v1/session/start', { method: 'POST', headers: { ...H, 'Content-Type': 'application/json' }, body: JSON.stringify({ scenarioId: SCEN.scenarioId, scenarioVersion: SCEN.version, language: conv }) })).json()).sessionId;
await new Promise(r => setTimeout(r, 1500));
const events = ['FACT:H_ONSET'];
const ctx = (stage = 'HistoryTaking') => ({ stage, stateId: 'S_PRESENTATION', events });
const results = [];

async function readFramed(r, name) {
  const reader = r.body.getReader(); let buf = Buffer.alloc(0), meta = null; const audio = []; const t0 = Date.now(); let tFirst = 0;
  while (true) { const { value, done } = await reader.read(); if (done) break; let c = Buffer.from(value);
    if (!meta) { buf = Buffer.concat([buf, c]); const nl = buf.indexOf(10); if (nl < 0) continue; meta = JSON.parse(buf.subarray(0, nl).toString('utf8')); c = buf.subarray(nl + 1); }
    if (c.length) { if (!tFirst) tFirst = Date.now() - t0; audio.push(c); } }
  const pcm = Buffer.concat(audio); fs.writeFileSync(path.join(OUT, name + '.wav'), pcmToWav(pcm));
  return { meta, audioSec: +(pcm.length / 48000).toFixed(2) };
}
function check(name, expectLang, meta, audioSec, extra = {}) {
  const text = meta?.text || ''; const lang = scriptLang(text);
  const words = text.split(/\s+/).filter(Boolean).length;
  // completeness heuristic: speech rate 1.3–4.2 words/s for the WHOLE line (a cut-off line has too little audio)
  const rate = audioSec > 0 ? words / audioSec : 99;
  const complete = audioSec > 0 && rate < 4.2;
  const r = { name, heard: meta?.heard, reply: text, replyLang: lang, expectLang, langOK: lang === expectLang, source: meta?.source, rejected: meta?.rejected || null, languageCommand: meta?.languageCommand || null, words, audioSec, wordsPerSec: +rate.toFixed(2), audioComplete: complete, ...extra };
  results.push(r); console.log(JSON.stringify(r));
}
async function voice(name, persona, clip, expectLang, stage) {
  const q = new URLSearchParams({ sessionId: sid, persona, lang: conv, langLock: lock, ctx: Buffer.from(JSON.stringify(ctx(stage))).toString('base64url') });
  const t0 = Date.now();
  const r = await fetch(`${BASE}/v1/voice?${q}`, { method: 'POST', headers: { ...H, 'Content-Type': 'application/octet-stream' }, body: fs.readFileSync(path.join(AUDIO, clip + '.wav')).subarray(44) });
  const { meta, audioSec } = await readFramed(r, name);
  // emulate the Unity client: explicit command → lock; otherwise Auto follows the student's language
  if (meta?.languageCommand) { lock = meta.languageCommand; conv = lock; }
  else if (!lock && meta?.heard) conv = scriptLang(meta.heard) || conv;
  check(name, expectLang, meta, audioSec, { totalMs: Date.now() - t0, intent: meta?.intent, investigationId: meta?.investigationId, factIds: meta?.factIds });
}
async function say(name, persona, kind, baseText, expectLang, extra = {}) {
  const t0 = Date.now();
  const r = await fetch(`${BASE}/v1/say-stream`, { method: 'POST', headers: { ...H, 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: sid, persona, kind, baseText, language: conv, langLock: lock, context: ctx(kind === 'discussion' ? 'ClinicalReasoning' : 'HistoryTaking'), ...extra }) });
  const { meta, audioSec } = await readFramed(r, name);
  check(name, expectLang, meta, audioSec, { totalMs: Date.now() - t0, kind });
}

// 1. Auto mode, app language English: student speaks Arabic from the first question
await voice('01_patient_ar_first_question', 'Patient', 'student_ar_onset', 'ar');
await voice('02_patient_ar_followup_radiation', 'Patient', 'student_ar_where', 'ar');
await voice('03_patient_mixed_ar_en_allergy', 'Patient', 'student_ar_mixed_allergy', 'ar');
await voice('04_patient_ar_reassurance', 'Patient', 'student_ar_reassure', 'ar');
// 2. Explicit switch to Arabic (to the doctor) → both characters Arabic, even for English input
await voice('05_doctor_ar_command', 'Instructor', 'student_ar_cmd', 'ar');
await voice('06_doctor_english_input_while_arabic_locked', 'Instructor', 'student_en_dr_hello', 'ar');
await voice('07_patient_english_input_while_arabic_locked', 'Patient', 'student_en_allergy', 'ar');
await voice('08_doctor_ar_mixed_ecg_request', 'Instructor', 'student_ar_dr_ecg', 'ar');
// 3. Director lines in Arabic: intervention, discussion, long debrief
const allergyLine = SCEN.interventions.find(i => i.id === 'I_ALLERGY_CHECK').lines[0];
await say('09_intervention_ar', 'Instructor', 'intervention', allergyLine, 'ar', { severity: 'ordinary' });
await say('10_discussion_ar', 'Instructor', 'discussion', SCEN.instructorLines.discussionIntro + ' ' + SCEN.discussion[0].question, 'ar');
await say('11_debrief_ar_long', 'Instructor', 'debrief', '', 'ar', { result: { met: ['Introduced self and confirmed identity', 'Asked about onset of the pain', 'Checked allergies'], missed: ['Did not ask about radiation of pain'], missedCritical: ['Did not obtain an ECG within 10 minutes'], interventions: ['Supervisor prompted the allergy check'], aiInterpreted: [], escalated: true, ecgSeconds: 720, hints: 1 } });
// 4. Switch back to English
await voice('12_doctor_en_command', 'Instructor', 'student_en_cmd', 'en');
await voice('13_patient_arabic_input_while_english_locked', 'Patient', 'student_ar_onset', 'en');

fs.writeFileSync(path.resolve(HERE, '../data/arabic-results.json'), JSON.stringify(results, null, 2));
const bad = results.filter(r => !r.langOK || !r.audioComplete);
console.log(`\n${results.length} turns · language correct ${results.filter(r => r.langOK).length}/${results.length} · audio complete ${results.filter(r => r.audioComplete).length}/${results.length}` + (bad.length ? ' · CHECK: ' + bad.map(b => b.name).join(', ') : ''));
await fetch(BASE + '/v1/session/end', { method: 'POST', headers: { ...H, 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: sid }) });
