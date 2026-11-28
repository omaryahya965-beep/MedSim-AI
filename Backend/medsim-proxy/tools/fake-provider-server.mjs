// Runs the REAL MedSim proxy (same routes, grounding, validation, sessions, streaming) with a FAKE provider, so the Unity
// client's complete voice path can be exercised end to end without an API key and without spending anything:
//   microphone → push-to-talk / hands-free → native voice turn → proxy → grounded reply → streamed PCM → AudioSource → lip sync.
// What is fake: speech recognition (the "heard" text comes from a queue the test fills) and the voice (a speech-like
// synthetic waveform, not words). Everything else is the production code path.
//
// Safety: no API key is loaded, outgoing network access is blocked, and the ledger is a temporary file, so this process
// cannot reach OpenAI or touch the real spending ledger.
//
// Usage:  node tools/fake-provider-server.mjs            (listens on 127.0.0.1:8787, control API on 127.0.0.1:8788)
//   POST http://127.0.0.1:8788/heard  {"heard":"take me to the operating room","lang":"english","fields":{...}}  → next utterance's transcript
//        (fields: optional classification a real model would return, e.g. {"addressedTo":"assistant","action":"hand_instrument"})
//   GET  http://127.0.0.1:8788/log                                                                  → what the proxy heard and voiced
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';

// ---- isolation (before the proxy is imported) ----
process.env.MEDSIM_SECRETS_FILE = path.join(os.tmpdir(), 'medsim-fake-provider-no-secrets.env');   // never the real secrets file
delete process.env.OPENAI_API_KEY;
process.env.MEDSIM_LEDGER_FILE = path.join(os.tmpdir(), `medsim-fake-ledger-${process.pid}.jsonl`);
const unityConfig = path.join(os.homedir(), 'AppData', 'LocalLow', 'MedSim AI', 'MedSim AI', 'medsim_ai_config.json');
try { process.env.MEDSIM_CLIENT_TOKEN = JSON.parse(fs.readFileSync(unityConfig, 'utf8')).clientSessionToken; } catch { process.env.MEDSIM_CLIENT_TOKEN ||= 'fake-token'; }
globalThis.fetch = async () => { throw new Error('network disabled in the fake-provider server'); };
globalThis.WebSocket = class { constructor() { throw new Error('network disabled in the fake-provider server'); } };

const openai = await import('../lib/openai.mjs');
const srv = await import('../server.mjs');

const heardQueue = [];
const log = [];
const note = e => { const row = { t: new Date().toISOString(), ...e }; log.push(row); console.log('[fake]', JSON.stringify(row)); };

function defaults(s) {
  if (!s || typeof s !== 'object') return null;
  if (s.enum) return s.enum[0];
  switch (Array.isArray(s.type) ? s.type[0] : s.type) {
    case 'object': { const o = {}; for (const [k, v] of Object.entries(s.properties || {})) o[k] = defaults(v); return o; }
    case 'array': return [];
    case 'boolean': return false;
    case 'number': case 'integer': return 0;
    default: return '';
  }
}

/** Speech-like test signal: voiced harmonics with a syllable-rate envelope (drives lip sync), length from the text. */
function speechLike(text) {
  const rate = 24000, sec = Math.min(7, Math.max(0.9, String(text).length * 0.055));
  const n = Math.floor(rate * sec), pcm = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) {
    const t = i / rate, env = Math.max(0, Math.sin(Math.PI * 4.2 * t)) ** 0.6 * Math.min(1, t * 8, (sec - t) * 8);
    const f0 = 190 + 25 * Math.sin(2 * Math.PI * 0.7 * t);
    const v = env * (0.55 * Math.sin(2 * Math.PI * f0 * t) + 0.3 * Math.sin(2 * Math.PI * 2 * f0 * t) + 0.15 * Math.sin(2 * Math.PI * 3 * f0 * t));
    pcm.writeInt16LE(Math.round(v * 0.32 * 32767), i * 2);
  }
  return pcm;
}

// ---- the fake provider ----
const ai = srv.ai;
ai.apiKey = 'fake-provider-no-network';            // "configured" so the production routes run; never sent anywhere
ai.json = async ({ schema, tag }) => { note({ kind: 'json', tag }); return { data: defaults(schema) }; };
ai.prewarmVoice = () => { };
srv.setClassifier(async (system, history, user, schema) => defaults(schema?.schema || schema));   // legacy text route (/v1/turn)
ai.speakLegacy = async text => ({ pcm: speechLike(text), transcript: text, ms: 10 });
ai.speak = async (text, { voice, onAudio, signal } = {}) => {
  const pcm = speechLike(text); note({ kind: 'speak', voice, text, audioSec: +(pcm.length / 48000).toFixed(2) });
  const t0 = Date.now(); let first = null;
  for (let o = 0; o < pcm.length; o += 4800) {                    // 100 ms chunks, streamed faster than real time
    if (signal?.aborted) return { pcm, transcript: text, ms: Date.now() - t0, firstAudioMs: first, cancelled: true };
    onAudio?.(pcm.subarray(o, Math.min(pcm.length, o + 4800))); first ??= Date.now() - t0;
    await new Promise(r => setTimeout(r, 40));
  }
  return { pcm, transcript: text, ms: Date.now() - t0, firstAudioMs: first ?? 0 };
};
let calls = 0;
openai.RealtimeUnderstanding.prototype.connect = async function () { };
openai.RealtimeUnderstanding.prototype.close = function () { this.closed = true; };
openai.RealtimeUnderstanding.prototype.noteSpoken = function () { };
openai.RealtimeUnderstanding.prototype.understand = async function (pcm, { schema }) {
  let rms = 0; const n = Math.floor(pcm.length / 2); for (let i = 0; i < n; i++) { const v = pcm.readInt16LE(i * 2) / 32768; rms += v * v; } rms = Math.sqrt(rms / Math.max(1, n));
  const next = heardQueue.shift() || { heard: '', lang: 'none' };
  const data = { ...defaults(schema), ...(next.fields || {}) }; data.heard = next.heard; data.spokenLanguage = next.heard ? (next.lang || 'english') : 'none';   // fields: the classification a real model would return (e.g. addressedTo/action)
  note({ kind: 'understand', audioSec: +(n / 24000).toFixed(2), audioRms: +rms.toFixed(4), heard: next.heard, lang: data.spokenLanguage });
  return { data, callId: 'fake-' + (++calls), ms: 30 };
};

// ---- start the real proxy routes + the control API ----
srv.loadScenarios(); srv.loadSurgery();
srv.createServer().listen(8787, '127.0.0.1', () => console.log(`FAKE-PROVIDER MedSim proxy http://127.0.0.1:8787 (no key, no network, ledger ${process.env.MEDSIM_LEDGER_FILE}) clinical=[${[...srv.loadScenarios().keys()]}] surgery=[${[...srv.surgeryScenarios.keys()]}]`));
http.createServer((req, res) => {
  let b = ''; req.on('data', c => b += c); req.on('end', () => {
    if (req.method === 'POST' && req.url === '/heard') { try { const j = JSON.parse(b || '{}'); heardQueue.push({ heard: String(j.heard || ''), lang: j.lang || 'english', fields: j.fields && typeof j.fields === 'object' ? j.fields : null }); note({ kind: 'queued', heard: j.heard }); } catch { } res.end('ok'); return; }
    if (req.method === 'POST' && req.url === '/reset') { heardQueue.length = 0; log.length = 0; note({ kind: 'reset' }); res.end('ok'); return; }
    if (req.url === '/log') { res.setHeader('content-type', 'application/json'); res.end(JSON.stringify(log)); return; }
    res.statusCode = 404; res.end();
  });
}).listen(8788, '127.0.0.1');
