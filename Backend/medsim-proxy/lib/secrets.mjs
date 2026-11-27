// Loads provider secrets for the proxy. Order: real environment variables, then a local secrets file that lives
// OUTSIDE the Unity project and the git repository (default: D:\Medical-Test\secrets\medsim.env, i.e. three levels
// above this backend). Values are never logged, returned over HTTP, or written anywhere else.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const DEFAULT_SECRETS_FILE = path.resolve(HERE, '../../../../secrets/medsim.env');

export function parseEnvFile(text) {
  const out = {};
  for (const raw of String(text).split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const i = line.indexOf('=');
    if (i <= 0) continue;
    const k = line.slice(0, i).trim();
    let v = line.slice(i + 1).trim();
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) v = v.slice(1, -1);
    if (/^[A-Z0-9_]+$/.test(k)) out[k] = v;
  }
  return out;
}

export function loadSecrets(env = process.env) {
  const file = env.MEDSIM_SECRETS_FILE || DEFAULT_SECRETS_FILE;
  let fromFile = {};
  try { if (fs.existsSync(file)) fromFile = parseEnvFile(fs.readFileSync(file, 'utf8')); } catch { /* unreadable → env only */ }
  const pick = k => (env[k] && String(env[k]).trim()) || fromFile[k] || '';
  return {
    openaiKey: pick('OPENAI_API_KEY'),
    clientToken: pick('MEDSIM_CLIENT_TOKEN'),
    source: env.OPENAI_API_KEY ? 'environment' : (fromFile.OPENAI_API_KEY ? 'secrets-file' : 'none'),
  };
}

/** Removes anything that looks like a provider key or bearer token from text destined for logs or clients. */
export function redact(s) {
  return String(s ?? '').replace(/Bearer\s+[A-Za-z0-9_\-.]+/gi, 'Bearer ***').replace(/sk-[A-Za-z0-9_\-]{8,}/g, 'sk-***');
}
