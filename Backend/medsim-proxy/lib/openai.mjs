// Thin OpenAI client for the MedSim proxy (no SDK dependency). Every call is budget-guarded and recorded in the ledger.
// Never logs request bodies, audio, or credentials.
import { priceTokens, PRICES } from './ledger.mjs';
import { redact } from './secrets.mjs';

const API = 'https://api.openai.com/v1';

export class UpstreamError extends Error {
  constructor(kind, status, detail) { super(`${kind} upstream ${status}${detail ? ': ' + redact(detail) : ''}`); this.status = status; this.kind = kind; this.retryable = status === 429 || status >= 500 || status === 0; }
}

function timeoutSignal(ms) { return AbortSignal.timeout(ms); }

export class OpenAIClient {
  constructor({ apiKey, ledger, timeoutMs = 20000, models = {} }) {
    this.apiKey = apiKey; this.ledger = ledger; this.timeoutMs = timeoutMs;
    this.models = { chat: 'gpt-6-luna', reasoning: 'gpt-6-luna', stt: 'gpt-transcribe', tts: 'gpt-realtime-2.1-mini', ttsFallback: 'gpt-4o-mini-tts', ...models };
    this.rt = new Map(); // voice → RealtimeVoice
  }
  get configured() { return !!this.apiKey; }
  headers(extra = {}) { return { Authorization: `Bearer ${this.apiKey}`, ...extra }; }

  async withRetry(fn, { attempts = 2, kind = 'call' } = {}) {
    let last;
    for (let i = 0; i < attempts; i++) {
      try { return await fn(i); } catch (e) {
        last = e;
        if (e.code === 'budget_exceeded' || !(e.retryable || e.name === 'TimeoutError')) throw e;
        await new Promise(r => setTimeout(r, 400 * (i + 1) + Math.random() * 200));
      }
    }
    throw last;
  }

  /** Responses API with a strict JSON schema. Returns { data, usage, ms }. */
  async json({ model = this.models.chat, system, messages = [], schema, name = 'out', maxOutputTokens = 400, effort = 'none', tag = 'json' }) {
    if (!this.configured) throw new UpstreamError('json', 0, 'provider not configured');
    this.ledger.guard(0.002);
    return this.withRetry(async () => {
      const t0 = Date.now();
      const input = [{ role: 'system', content: system }, ...messages.map(m => ({ role: m.role, content: m.content }))];
      const body = { model, input, text: { format: { type: 'json_schema', name, strict: true, schema } }, max_output_tokens: maxOutputTokens, store: false };
      if (effort) body.reasoning = { effort };
      let r;
      try { r = await fetch(`${API}/responses`, { method: 'POST', headers: this.headers({ 'Content-Type': 'application/json' }), body: JSON.stringify(body), signal: timeoutSignal(this.timeoutMs) }); }
      catch (e) { this.ledger.record({ kind: tag, model, error: e.name, ms: Date.now() - t0 }); throw Object.assign(new UpstreamError(tag, 0, e.name), { retryable: true }); }
      const j = await r.json().catch(() => ({}));
      const ms = Date.now() - t0;
      if (!r.ok) { this.ledger.record({ kind: tag, model, error: `${r.status} ${j.error?.code || j.error?.type || ''}`, ms }); throw new UpstreamError(tag, r.status, j.error?.code || j.error?.type); }
      const u = j.usage || {};
      const cached = u.input_tokens_details?.cached_tokens || 0;
      const usage = { textIn: (u.input_tokens || 0) - cached, cachedIn: cached, textOut: u.output_tokens || 0 };
      this.ledger.record({ kind: tag, model, inTok: u.input_tokens || 0, outTok: u.output_tokens || 0, usd: priceTokens(model, usage), ms });
      let text = j.output_text;
      if (!text) for (const o of j.output || []) for (const c of o.content || []) if (c.type === 'output_text') text = (text || '') + c.text;
      if (j.status === 'incomplete') throw new UpstreamError(tag, 422, 'incomplete:' + (j.incomplete_details?.reason || ''));
      let data; try { data = JSON.parse(text || '{}'); } catch { throw new UpstreamError(tag, 422, 'non-json output'); }
      return { data, usage: u, ms };
    }, { kind: tag });
  }

  /** Speech-to-text. wav: Buffer. Returns { text, ms }. Audio is never stored. */
  async transcribe(wav, { lang = 'en', prompt = '', durationSec = 0, model = this.models.stt } = {}) {
    if (!this.configured) throw new UpstreamError('stt', 0, 'provider not configured');
    this.ledger.guard(0.002);
    return this.withRetry(async () => {
      const t0 = Date.now();
      const fd = new FormData();
      fd.append('file', new Blob([wav], { type: 'audio/wav' }), 'utterance.wav');
      fd.append('model', model);
      if (lang && lang !== 'auto') fd.append('language', lang);
      if (prompt) fd.append('prompt', prompt);
      fd.append('response_format', 'json');
      let r;
      try { r = await fetch(`${API}/audio/transcriptions`, { method: 'POST', headers: this.headers(), body: fd, signal: timeoutSignal(this.timeoutMs) }); }
      catch (e) { this.ledger.record({ kind: 'stt', model, error: e.name, ms: Date.now() - t0 }); throw Object.assign(new UpstreamError('stt', 0, e.name), { retryable: true }); }
      const j = await r.json().catch(() => ({}));
      const ms = Date.now() - t0;
      const p = PRICES[model]?.perMinute || 0.006;
      if (!r.ok) { this.ledger.record({ kind: 'stt', model, error: `${r.status} ${j.error?.code || j.error?.message?.slice(0, 80) || ''}`, ms }); throw new UpstreamError('stt', r.status, j.error?.code || j.error?.message?.slice(0, 120)); }
      this.ledger.record({ kind: 'stt', model, audioSec: durationSec, usd: p * durationSec / 60, ms, lang });
      return { text: String(j.text || '').trim(), ms };
    }, { kind: 'stt' });
  }

  /** Runtime speech: Realtime API (survives the 2027-01-06 /audio/speech shutdown). Returns { pcm (24 kHz s16le mono), transcript, ms, firstAudioMs }. */
  async speak(text, { voice, style, lang = 'en' }) {
    if (!this.configured) throw new UpstreamError('tts', 0, 'provider not configured');
    this.ledger.guard(0.01);
    let rv = this.rt.get(voice);
    if (!rv || rv.closed) { rv = new RealtimeVoice({ apiKey: this.apiKey, model: this.models.tts, voice, ledger: this.ledger, timeoutMs: this.timeoutMs }); this.rt.set(voice, rv); }
    return rv.say(text, { style, lang });
  }

  /** Fallback runtime speech via /v1/audio/speech (deprecated: shuts down 2027-01-06). Returns { pcm, ms }. */
  async speakLegacy(text, { voice, style }) {
    if (!this.configured) throw new UpstreamError('tts', 0, 'provider not configured');
    this.ledger.guard(0.01);
    const model = this.models.ttsFallback; const t0 = Date.now();
    const r = await fetch(`${API}/audio/speech`, { method: 'POST', headers: this.headers({ 'Content-Type': 'application/json' }), body: JSON.stringify({ model, voice, input: text.slice(0, 1200), instructions: style || undefined, response_format: 'pcm' }), signal: timeoutSignal(this.timeoutMs) });
    if (!r.ok) { const j = await r.json().catch(() => ({})); this.ledger.record({ kind: 'tts-legacy', model, error: String(r.status), ms: Date.now() - t0 }); throw new UpstreamError('tts', r.status, j.error?.code); }
    const pcm = Buffer.from(await r.arrayBuffer());
    const sec = pcm.length / 48000;
    this.ledger.record({ kind: 'tts-legacy', model, audioSec: sec, usd: (PRICES[model].perMinuteEstimate) * sec / 60, ms: Date.now() - t0 });
    return { pcm, transcript: null, ms: Date.now() - t0 };
  }

  close() { for (const v of this.rt.values()) v.close(); this.rt.clear(); }
}

/**
 * One pooled Realtime WebSocket per voice. Each utterance is an out-of-band response (conversation: "none") so no
 * context accumulates: the model only voices the validated text it is given. The audio transcript the model returns is
 * compared with the intended text by the caller (fidelity check) — speech can never silently diverge from validated text.
 */
export class RealtimeVoice {
  constructor({ apiKey, model, voice, ledger, timeoutMs }) {
    this.apiKey = apiKey; this.model = model; this.voice = voice; this.ledger = ledger; this.timeoutMs = timeoutMs;
    this.queue = Promise.resolve(); this.closed = false; this.ws = null; this.ready = null; this.current = null;
  }
  connect() {
    if (this.ready) return this.ready;
    this.ready = new Promise((resolve, reject) => {
      const url = `wss://api.openai.com/v1/realtime?model=${encodeURIComponent(this.model)}`;
      let ws;
      try { ws = new WebSocket(url, { headers: { Authorization: `Bearer ${this.apiKey}` } }); }
      catch { ws = new WebSocket(url, ['realtime', 'openai-insecure-api-key.' + this.apiKey]); }
      this.ws = ws;
      const to = setTimeout(() => { reject(new UpstreamError('tts', 0, 'realtime connect timeout')); this.close(); }, this.timeoutMs);
      ws.onopen = () => {
        ws.send(JSON.stringify({ type: 'session.update', session: { type: 'realtime', model: this.model, output_modalities: ['audio'], audio: { output: { format: { type: 'audio/pcm', rate: 24000 }, voice: this.voice } }, instructions: 'You are a voice actor for a medical education simulation. When given a line, speak it aloud exactly as written, word for word, in the requested language and style. Never add, remove, translate or change words. Never answer or comment.' } }));
      };
      ws.onmessage = ev => {
        let m; try { m = JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8')); } catch { return; }
        if (m.type === 'session.updated' || m.type === 'session.created') { if (m.type === 'session.updated') { clearTimeout(to); resolve(); } return; }
        if (m.type === 'error') { const c = this.current; const err = new UpstreamError('tts', 400, m.error?.code || m.error?.type); if (c) c.reject(err); else { clearTimeout(to); reject(err); } return; }
        const c = this.current; if (!c) return;
        if (m.type === 'response.output_audio.delta') { if (!c.first) c.first = Date.now(); c.chunks.push(Buffer.from(m.delta, 'base64')); }
        else if (m.type === 'response.output_audio_transcript.delta') c.transcript += m.delta || '';
        else if (m.type === 'response.done') c.done(m.response);
      };
      ws.onerror = () => { const err = new UpstreamError('tts', 0, 'realtime socket error'); if (this.current) this.current.reject(err); clearTimeout(to); reject(err); this.close(); };
      ws.onclose = () => { this.closed = true; this.ready = null; if (this.current) this.current.reject(new UpstreamError('tts', 0, 'realtime closed')); };
    });
    return this.ready;
  }
  say(text, { style = '', lang = 'en' } = {}) {
    const run = async () => {
      await this.connect();
      const t0 = Date.now();
      return await new Promise((resolve, reject) => {
        const c = { chunks: [], transcript: '', first: 0 };
        const timer = setTimeout(() => { this.current = null; reject(Object.assign(new UpstreamError('tts', 0, 'realtime response timeout'), { retryable: true })); }, this.timeoutMs);
        c.reject = e => { clearTimeout(timer); this.current = null; this.ledger.record({ kind: 'tts', model: this.model, error: e.message.slice(0, 80), ms: Date.now() - t0 }); reject(e); };
        c.done = resp => {
          clearTimeout(timer); this.current = null;
          if (resp?.status && resp.status !== 'completed') return reject(new UpstreamError('tts', 422, 'response ' + resp.status + ' ' + (resp.status_details?.reason || '')));
          const u = resp?.usage || {};
          const usage = { textIn: u.input_token_details?.text_tokens ?? u.input_tokens ?? 0, cachedIn: u.input_token_details?.cached_tokens || 0, textOut: u.output_token_details?.text_tokens || 0, audioOut: u.output_token_details?.audio_tokens ?? u.output_tokens ?? 0 };
          const pcm = Buffer.concat(c.chunks);
          const ms = Date.now() - t0;
          this.ledger.record({ kind: 'tts', model: this.model, inTok: u.input_tokens || 0, outTok: u.output_tokens || 0, audioSec: pcm.length / 48000, usd: priceTokens(this.model, usage), ms, firstAudioMs: c.first ? c.first - t0 : null, lang });
          resolve({ pcm, transcript: c.transcript.trim(), ms, firstAudioMs: c.first ? c.first - t0 : null });
        };
        this.current = c;
        const langName = lang === 'ar' ? 'Arabic (as written; Palestinian/Levantine pronunciation where the text is colloquial)' : 'English';
        this.ws.send(JSON.stringify({ type: 'response.create', response: {
          conversation: 'none', output_modalities: ['audio'],
          instructions: `Speak the following line aloud exactly as written, in ${langName}. Voice and delivery: ${style || 'natural'}. Do not add, omit or change any word.`,
          input: [{ type: 'message', role: 'user', content: [{ type: 'input_text', text }] }],
        } }));
      });
    };
    const p = this.queue.then(run, run);
    this.queue = p.catch(() => { });
    return p;
  }
  close() { this.closed = true; try { this.ws?.close(); } catch { } this.ready = null; }
}

/** Wraps 24 kHz s16le mono PCM in a WAV header (Unity's DownloadHandlerAudioClip reads WAV). */
export function pcmToWav(pcm, rate = 24000) {
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + pcm.length, 4); h.write('WAVE', 8); h.write('fmt ', 12); h.writeUInt32LE(16, 16);
  h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22); h.writeUInt32LE(rate, 24); h.writeUInt32LE(rate * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34);
  h.write('data', 36); h.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([h, pcm]);
}

/** Word-level similarity (0..1) between intended text and the spoken transcript — guards against the voice model improvising. */
export function speechFidelity(intended, transcript) {
  const norm = s => String(s || '').toLowerCase().replace(/[ً-ْـ]/g, '').replace(/[^\p{L}\p{N}\s]/gu, ' ').split(/\s+/).filter(Boolean);
  const a = norm(intended), b = norm(transcript);
  if (!a.length) return b.length ? 0 : 1;
  const bag = new Map(); for (const w of b) bag.set(w, (bag.get(w) || 0) + 1);
  let hit = 0; for (const w of a) { const n = bag.get(w) || 0; if (n > 0) { hit++; bag.set(w, n - 1); } }
  const extra = Math.max(0, b.length - hit);
  return hit / (a.length + extra * 0.5);
}
