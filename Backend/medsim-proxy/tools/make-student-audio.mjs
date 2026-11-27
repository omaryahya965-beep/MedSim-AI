// Generates short synthetic "student microphone" WAV clips (runtime TTS) used to drive the real STT path in
// desktop end-to-end tests (no headset microphone available to the automation). Billed against the budget (~$0.002 each).
// Usage: node tools/make-student-audio.mjs <outDir>
import fs from 'node:fs';
import path from 'node:path';
import { loadSecrets } from '../lib/secrets.mjs';
import { Ledger } from '../lib/ledger.mjs';
import { OpenAIClient, pcmToWav } from '../lib/openai.mjs';

const out = process.argv[2] || 'data/student-audio';
fs.mkdirSync(out, { recursive: true });
const ai = new OpenAIClient({ apiKey: loadSecrets().openaiKey, ledger: new Ledger({ budgetUsd: Number(process.env.MEDSIM_BUDGET_USD || 2) }) });
const only = process.argv[3]; const lines = [
  ['student_en_intro', 'en', 'alloy', 'Hello Mister Reyes, I am a medical student working with Doctor Hassan. Can you tell me when the pain started?'],
  ['student_ar_onset', 'ar', 'coral', 'مرحبا عمو، أنا طالبة طب. من أيمتى بلش معك الوجع؟'],
];
for (const [name, lang, voice, text] of lines.filter(l => !only || l[0] === only)) {
  const r = await ai.speak(text, { voice, lang, style: 'a young medical student speaking clearly and politely' });
  fs.writeFileSync(path.join(out, name + '.wav'), pcmToWav(r.pcm));
  console.log(name, (r.pcm.length / 48000).toFixed(2) + 's', 'transcript:', r.transcript);
}
ai.close();
process.exit(0);
