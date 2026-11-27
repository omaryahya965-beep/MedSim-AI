# AI Setup — Live AI for the doctor and the patient (v0.2)

Both characters understand natural speech and answer with **runtime-generated, scenario-grounded speech** from OpenAI. Every call goes through the local **MedSim proxy** (`Backend/medsim-proxy`). The OpenAI key never reaches Unity, builds, logs or the repository.

> Status: **verified against the real OpenAI API on 2026-11-27.** See [LIVE_AI_TEST_REPORT.md](LIVE_AI_TEST_REPORT.md).

## 1. Provide the key securely (one time)

The proxy reads secrets from the environment first, then from a local file **outside the project and the git repository**:

```
D:\Medical-Test\secrets\medsim.env      (ACL: your Windows user only)
```

The file contains two lines:
```
OPENAI_API_KEY=<your key>
MEDSIM_CLIENT_TOKEN=<long random string shared with the Unity app>
```

**Do not paste keys into chat, source code, Unity assets or documents.**

### Changing the key
1. Open `D:\Medical-Test\secrets\medsim.env` in Notepad and replace the `OPENAI_API_KEY=` value.
2. Restart the proxy.

You can also set `OPENAI_API_KEY` as a Windows user environment variable: Start → "Edit environment variables for your account" → New. An environment variable overrides the file.

> **Security note:** the key originally provided for this project was pasted into a chat conversation. Treat it as exposed: rotate it at platform.openai.com → API keys once testing is complete, then update the secrets file.

To create a new client token (Unity ↔ proxy shared secret):
```
node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"
```
Put it in `MEDSIM_CLIENT_TOKEN=` and re-run step 3.

## 2. Start the proxy
```
cd "D:\Medical-Test\My project\Backend\medsim-proxy"
node server.mjs
```

The proxy listens on `http://127.0.0.1:8787` by default (localhost only). Set `MEDSIM_HOST=0.0.0.0` only if a standalone headset must reach the PC; in that case put it behind HTTPS first. The proxy needs Node 22+ and has no npm dependencies.

`GET /health` reports:
- the loaded scenarios,
- whether a provider key is configured (yes/no only),
- the models in use,
- the remaining test budget.

## 3. Point Unity at the proxy
```
node tools/write-unity-config.mjs http://127.0.0.1:8787 en
```

This writes `%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\medsim_ai_config.json` with the **client token only**. If this file is missing, the app runs text-only and says "AI OFFLINE — text only".

## 4. Models (verified on this account, 2026-11-27)
| Purpose | Model | Endpoint | Price (official pages, read 2026-11-27) |
|---|---|---|---|
| Understanding + reply generation, both personas (strict JSON schema) | `gpt-6-luna` | `/v1/responses` with `text.format: json_schema strict`, `reasoning.effort: none` | $0.10 / $0.50 per 1M input/output tokens |
| Speech-to-text, English + Arabic | `gpt-transcribe` | `/v1/audio/transcriptions` | $0.0045 / min |
| Runtime speech | `gpt-realtime-2.1-mini` | Realtime API over a server-side WebSocket; out-of-band responses (`conversation: "none"`) | Audio output $20 per 1M tokens (about $0.002–0.004 per spoken line in our measurements) |
| Speech fallback (exact text) | `gpt-4o-mini-tts` | `/v1/audio/speech` (**deprecated; shuts down 2027-01-06**) | about $0.015 / min |

All of these can be overridden by environment variables: `MEDSIM_CHAT_MODEL`, `MEDSIM_STT_MODEL`, `MEDSIM_TTS_MODEL`, `MEDSIM_TTS_FALLBACK_MODEL`.

**Voices** (can be overridden with `MEDSIM_VOICE_PATIENT` / `MEDSIM_VOICE_INSTRUCTOR`):
- Dr. Hassan: **marin**
- Mr. Reyes: **cedar**

**Why Realtime for speech?** OpenAI is retiring every `/v1/audio/speech` model on 2027-01-06, and its named replacement (`gpt-realtime-2.1-mini`) only runs on the Realtime API. MedSim uses Realtime as a *voice actor*: it is told to read a line exactly as written, and it returns its own transcript of what it said. The proxy compares that transcript with the validated text (`speechFidelity`). Below 0.75, the line is re-synthesised with the exact-text fallback. Measured fidelity was 1.00 for English and Palestinian Arabic.

## 5. Spending limits (enforced in the application)
- **Hard budget cap:** `MEDSIM_BUDGET_USD` (default **2.00**). Every call is priced from the provider's reported usage (or a conservative estimate for audio) and appended to `Backend/medsim-proxy/data/usage.jsonl`, which is git-ignored. Once the cap is reached, calls are refused with HTTP 402 and Unity shows "AI test budget reached". **The OpenAI dashboard budget alert is not a cap; this is.**
- **Rate limits:** 40 AI requests per minute in total; 160 turns per session.
- **Size limits:** utterances up to 600 characters; spoken lines up to 700 characters; microphone clips up to 30 s (Unity records at most 15 s per push-to-talk).
- **Timeouts and retries:** upstream timeout 20 s, with at most one retry, and only for 429 or 5xx responses.
- **No per-frame calls.** AI is called only when the student speaks, when the supervisor decides to speak, and for the welcome and the debrief.
- **Usage summary:** `GET /v1/usage` (requires the client token).

## 6. How grounding works (summary — details in ARCHITECTURE.md)
1. Unity sends **ids only**: scenario id/version, persona, the student's words, stage, patient-state id, and the ids of events that have already happened. It never sends clinical text, and the proxy ignores any it receives.
2. The proxy loads the authoritative scenario itself. It gives the model a persona brief, the approved facts, the current authored patient state, and (for the doctor) only the findings the student has already obtained.
3. The model returns strict JSON: chosen fact ids, intent, concepts, and a natural reply.
4. **Deterministic grounding check** (`lib/grounding.mjs`). Every number and every clinical term in the reply must already appear in the approved corpus for that turn. Diagnoses are blocked before the feedback stage, dosing is blocked, and meta-talk is blocked. If any check fails, the reply is replaced with approved scenario text (`source: approved-fallback`).
5. The reply is spoken by runtime TTS.
6. Unity's deterministic engine records events. AI-only recognitions are tagged `AI_INTENT` and shown in the debrief as "AI-interpreted — for instructor review".

## 7. Failure behaviour
If the proxy is down, the key is invalid, the budget is exhausted, or a call times out, Unity shows a clear status message. The characters then answer **text-only** (subtitles) from deterministic scenario text. **No pre-recorded dialogue exists or is played**, and the app never presents offline text as Live AI.

## 8. Tests
- Offline, no key, no cost: `node --test server.test.mjs live.test.mjs`
- Live, billed against the budget: `node tools/live-harness.mjs patient instructor arabic states speech failure`, with the proxy running.
