// Application-side spending guard. Every upstream call is priced (from reported usage where the provider returns it,
// otherwise from a conservative estimate) and appended to data/usage.jsonl. Once the cumulative estimate reaches the
// budget, further provider calls are refused with a clear error. This is a HARD cap enforced in our code — a dashboard
// alert at OpenAI is not a spending limit.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));

// USD per 1M tokens unless noted. Source: developers.openai.com model pages, read 2026-11-27. Update if prices change.
export const PRICES = {
  'gpt-6-luna': { textIn: 0.10, cachedIn: 0.01, textOut: 0.50 },
  'gpt-6.2-sol': { textIn: 2.00, cachedIn: 0.10, textOut: 10.00 },
  'gpt-realtime-2.1-mini': { textIn: 0.60, cachedIn: 0.06, textOut: 2.40, audioIn: 10.00, audioOut: 20.00 },
  'gpt-4o-mini-tts': { textIn: 0.60, audioOut: 12.00, perMinuteEstimate: 0.015 },
  'gpt-transcribe': { perMinute: 0.0045 },
  'gpt-4o-transcribe': { perMinute: 0.006 },
  'gpt-4o-mini-transcribe': { perMinute: 0.003 },
};

export class BudgetExceededError extends Error { constructor(msg) { super(msg); this.code = 'budget_exceeded'; } }

export class Ledger {
  constructor({ budgetUsd = 2, file = path.resolve(HERE, '../data/usage.jsonl'), perRequestMaxUsd = 0.05 } = {}) {
    this.budgetUsd = budgetUsd; this.file = file; this.perRequestMaxUsd = perRequestMaxUsd;
    this.spent = 0; this.count = 0;
    try {
      fs.mkdirSync(path.dirname(file), { recursive: true });
      if (fs.existsSync(file)) for (const l of fs.readFileSync(file, 'utf8').split('\n')) { if (!l.trim()) continue; try { const e = JSON.parse(l); this.spent += e.usd || 0; this.count++; } catch { } }
    } catch { }
  }
  remaining() { return Math.max(0, this.budgetUsd - this.spent); }
  /** Throws before a call if the budget (with a margin for this call) is exhausted. */
  guard(expectedUsd = 0.005) {
    if (this.spent + Math.min(expectedUsd, this.perRequestMaxUsd) > this.budgetUsd)
      throw new BudgetExceededError(`MedSim test budget reached ($${this.spent.toFixed(4)} of $${this.budgetUsd}). Ask the project owner to approve more.`);
  }
  record(entry) {
    const e = { t: new Date().toISOString(), ...entry, usd: Number((entry.usd || 0).toFixed(6)) };
    this.spent += e.usd; this.count++;
    try { fs.appendFileSync(this.file, JSON.stringify(e) + '\n'); } catch { }
    return e;
  }
  summary() {
    const rows = [];
    try { for (const l of fs.readFileSync(this.file, 'utf8').split('\n')) if (l.trim()) rows.push(JSON.parse(l)); } catch { }
    const by = {};
    for (const r of rows) {
      const k = r.model + ' ' + r.kind; by[k] ??= { requests: 0, usd: 0, inTok: 0, outTok: 0, audioSec: 0, errors: 0, latencyMs: [] };
      const b = by[k]; b.requests++; b.usd += r.usd || 0; b.inTok += r.inTok || 0; b.outTok += r.outTok || 0; b.audioSec += r.audioSec || 0; if (r.error) b.errors++; if (r.ms) b.latencyMs.push(r.ms);
    }
    return { budgetUsd: this.budgetUsd, spentUsd: Number(this.spent.toFixed(4)), requests: rows.length, byModel: by };
  }
}

export function priceTokens(model, u = {}) {
  const p = PRICES[model] || {};
  const M = 1e6;
  return ((u.textIn || 0) * (p.textIn || 0) + (u.cachedIn || 0) * (p.cachedIn || 0) + (u.textOut || 0) * (p.textOut || 0)
    + (u.audioIn || 0) * (p.audioIn || 0) + (u.audioOut || 0) * (p.audioOut || 0)) / M;
}
