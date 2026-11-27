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
  ['student_en_allergy', 'en', 'alloy', 'Are you allergic to any medications?'],
  ['student_en_escalate', 'en', 'alloy', 'I would like to call the cardiology registrar and activate the cath lab now.'],
  ['student_en_stemi_q', 'en', 'alloy', 'Do you think this could be a STEMI?'],
  ['student_en_dr_ecg', 'en', 'alloy', 'Doctor Hassan, can I get an ECG for him please?'],
  ['student_en_dr_next', 'en', 'alloy', 'What should I do next?'],
  ['student_en_dr_hint', 'en', 'alloy', 'Can you give me a hint?'],
  ['student_en_dr_mi', 'en', 'alloy', 'I think he is having a heart attack.'],
  ['student_en_dr_hello', 'en', 'alloy', 'Hello Doctor Hassan, nice to meet you.'],
  ['student_ar_cmd', 'ar', 'coral', 'دكتورة، خلينا نحكي بالعربي لو سمحتي.'],
  ['student_ar_where', 'ar', 'coral', 'طيب وين بالزبط الوجع؟ بيمتد لمحل تاني؟'],
  ['student_ar_mixed_allergy', 'ar', 'coral', 'عندك أي allergy لأي دوا؟'],
  ['student_ar_dr_ecg', 'ar', 'coral', 'دكتورة، بدي أعمل ECG للمريض هلّق.'],
  ['student_ar_reassure', 'ar', 'coral', 'ما تخاف يا عمو، إحنا معك ورح نعتني فيك.'],
  ['student_en_cmd', 'en', 'alloy', 'Can we switch back to English please?'],
  ['student_ar_onset', 'ar', 'coral', 'مرحبا عمو، أنا طالبة طب. من أيمتى بلش معك الوجع؟'],
];
for (const [name, lang, voice, text] of lines.filter(l => !only || l[0] === only)) {
  const r = await ai.speak(text, { voice, lang, style: 'a young medical student speaking clearly and politely' });
  fs.writeFileSync(path.join(out, name + '.wav'), pcmToWav(r.pcm));
  console.log(name, (r.pcm.length / 48000).toFixed(2) + 's', 'transcript:', r.transcript);
}
ai.close();
process.exit(0);
