// Sustained vowel clips in the characters' runtime voices, for uLipSync phoneme calibration (Unity AgentScripts/LipSyncCalibrate.cs).
// Billed (small, ~$0.01 total). Usage: node tools/make-vowel-audio.mjs
import fs from 'node:fs';
import path from 'node:path';
import { loadSecrets } from '../lib/secrets.mjs';
import { Ledger } from '../lib/ledger.mjs';
import { OpenAIClient, pcmToWav } from '../lib/openai.mjs';
const out = 'data/lipsync'; fs.mkdirSync(out, { recursive: true });
const ai = new OpenAIClient({ apiKey: loadSecrets().openaiKey, ledger: new Ledger({ budgetUsd: Number(process.env.MEDSIM_BUDGET_USD || 2) }) });
const vowels = { A: 'Aaaaaaah.', I: 'Eeeeeeee.', U: 'Ooooooo.', E: 'Ehhhhhhh.', O: 'Ohhhhhhh.', N: 'Mmmmmmmm.' };
const style = 'holding one long, steady, relaxed vowel sound at normal speaking volume';
for (const voice of ['cedar', 'marin']) for (const [k, text] of Object.entries(vowels)) {
  let r; for (let k2 = 0; k2 < 3 && !r; k2++) { try { r = await ai.speak(text, { voice, lang: 'en', style }); } catch (e) { console.log('retry', voice, k, e.message); } }
  if (!r) continue;
  fs.writeFileSync(path.join(out, `${voice}_${k}.wav`), pcmToWav(r.pcm));
  console.log(voice, k, (r.pcm.length / 48000).toFixed(2) + 's', r.transcript);
}
ai.close(); process.exit(0);
