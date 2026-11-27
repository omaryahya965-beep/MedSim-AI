// Writes the NON-SECRET Unity client config (proxy URL + client session token) for this Windows user.
// The OpenAI key is never written here; it stays in the proxy's secrets file / environment.
// Usage: node tools/write-unity-config.mjs [http://127.0.0.1:8787] [en|ar]
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { loadSecrets } from '../lib/secrets.mjs';

const endpoint = process.argv[2] || 'http://127.0.0.1:8787';
const language = process.argv[3] === 'ar' ? 'ar' : 'en';
const { clientToken } = loadSecrets();
if (!clientToken) { console.error('MEDSIM_CLIENT_TOKEN missing in the secrets file/environment.'); process.exit(1); }
const dir = path.join(os.homedir(), 'AppData', 'LocalLow', 'MedSim AI', 'MedSim AI');
fs.mkdirSync(dir, { recursive: true });
const file = path.join(dir, 'medsim_ai_config.json');
fs.writeFileSync(file, JSON.stringify({ proxyEndpoint: endpoint, clientSessionToken: clientToken, preferOnline: true, timeoutMs: 25000, language }, null, 2));
console.log('Wrote', file, '(client token only; no provider key)');
