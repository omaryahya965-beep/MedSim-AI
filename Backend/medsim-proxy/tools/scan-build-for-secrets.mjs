// Scans a build (or any folder) for provider-key material. Prints only counts/paths — never the secret itself.
// Usage: node tools/scan-build-for-secrets.mjs <folder>
import fs from 'node:fs';
import path from 'node:path';
import { loadSecrets } from '../lib/secrets.mjs';

const root = process.argv[2];
const { openaiKey, clientToken } = loadSecrets();
const needles = [
  ['actual OpenAI key', openaiKey && Buffer.from(openaiKey)],
  ['OpenAI key (UTF-16)', openaiKey && Buffer.from(openaiKey, 'utf16le')],
  ['client session token', clientToken && Buffer.from(clientToken)],
  ['OPENAI_API_KEY name', Buffer.from('OPENAI_API_KEY')],
].filter(n => n[1]);
const pattern = /sk-(proj-)?[A-Za-z0-9_\-]{20,}/;
let files = 0, bytes = 0; const hits = [];
function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) { walk(p); continue; }
    const buf = fs.readFileSync(p); files++; bytes += buf.length;
    for (const [name, n] of needles) if (buf.indexOf(n) >= 0) hits.push(`${name} in ${path.relative(root, p)}`);
    if (pattern.test(buf.toString('latin1'))) hits.push(`sk- key pattern in ${path.relative(root, p)}`);
  }
}
walk(root);
console.log(`Scanned ${files} files (${(bytes / 1048576).toFixed(0)} MB) in ${root}`);
console.log(hits.length ? 'FOUND:\n' + hits.join('\n') : 'No provider key, client token, key pattern or OPENAI_API_KEY reference found.');
process.exit(hits.some(h => !h.startsWith('OPENAI_API_KEY')) ? 1 : 0);
