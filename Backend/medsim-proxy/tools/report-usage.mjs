// Summarises data/usage.jsonl (cost ledger) into a markdown table: requests, tokens, audio seconds, cost, latency, errors.
// Usage: node tools/report-usage.mjs [--since ISO]   (prints markdown; contains no credentials)
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const since = process.argv.includes('--since') ? new Date(process.argv[process.argv.indexOf('--since') + 1]) : null;
const rows = fs.readFileSync(path.resolve(HERE, '../data/usage.jsonl'), 'utf8').trim().split('\n').map(l => JSON.parse(l)).filter(r => !since || new Date(r.t) >= since);
const pct = (a, p) => { if (!a.length) return '-'; const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.floor(p * s.length))]; };
const by = new Map();
for (const r of rows) {
  const k = `${r.model} | ${r.kind}`; if (!by.has(k)) by.set(k, { n: 0, err: 0, usd: 0, inTok: 0, outTok: 0, audio: 0, ms: [], first: [] });
  const b = by.get(k); b.n++; if (r.error) b.err++; b.usd += r.usd || 0; b.inTok += r.inTok || 0; b.outTok += r.outTok || 0; b.audio += r.audioSec || 0; if (r.ms && !r.error) b.ms.push(r.ms); if (r.firstAudioMs) b.first.push(r.firstAudioMs);
}
let out = '| Model | Call | Requests | Errors | Input tok | Output tok | Audio s | Cost USD | Latency p50 / p90 ms | First audio p50 ms |\n|---|---|---|---|---|---|---|---|---|---|\n';
let total = 0, n = 0, e = 0;
for (const [k, b] of [...by.entries()].sort()) {
  const [m, kind] = k.split(' | ');
  out += `| ${m} | ${kind} | ${b.n} | ${b.err} | ${b.inTok} | ${b.outTok} | ${b.audio.toFixed(1)} | ${b.usd.toFixed(4)} | ${pct(b.ms, 0.5)} / ${pct(b.ms, 0.9)} | ${b.first.length ? pct(b.first, 0.5) : '-'} |\n`;
  total += b.usd; n += b.n; e += b.err;
}
out += `\n**Total: ${n} requests, ${e} errors, estimated cost $${total.toFixed(4)}** (from provider-reported token usage where available; audio STT/legacy-TTS estimated from duration).\n`;
console.log(out);
