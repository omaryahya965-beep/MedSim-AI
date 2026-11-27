# AI Setup — Live, scenario-grounded conversation

## What you need (credentials)
**One OpenAI API key with billing enabled.** That's the only credential. It goes in an environment variable on the machine running the proxy, never in Unity, the APK or any file in this repository.

| Variable | Required | Purpose |
|---|---|---|
| `OPENAI_API_KEY` | yes | Provider key (server-side only) |
| `MEDSIM_CLIENT_TOKEN` | strongly recommended | Shared secret the headset sends in `X-MedSim-Client`; requests without it get 401 |
| `MEDSIM_CHAT_MODEL` | no (default `gpt-6-luna`) | Structured-output classification |
| `MEDSIM_STT_MODEL` | no (default `gpt-transcribe`) | Speech-to-text |
| `MEDSIM_TTS_MODEL` | no (default `gpt-4o-mini-tts`) | Text-to-speech, see the warning below |
| `MEDSIM_VOICE_PATIENT` / `MEDSIM_VOICE_INSTRUCTOR` | no (`ash` / `marin`) | Distinct voices |
| `PORT` | no (8787) | |

**Model status (checked 2026-11-26 on developers.openai.com):**
- `gpt-6-luna` is current and supports Structured Outputs on Chat Completions.
- `gpt-transcribe` is the current transcription model. I could not confirm Arabic support from its documentation, and the multipart field name for its `languages` parameter is unverified.
- **Every model accepted by `/v1/audio/speech` is deprecated, with shutdown on 2027-01-06.** OpenAI's replacement (`gpt-realtime-2.1-mini`) uses the Realtime API, which this proxy does not implement yet.

All of these are configurable and are verified only by reading the documentation. **No billable call was made during development.**

## Run the proxy
```
cd Backend/medsim-proxy
set OPENAI_API_KEY=...            (PowerShell: $env:OPENAI_API_KEY="...")
set MEDSIM_CLIENT_TOKEN=<long random string>
node server.mjs
```
The proxy loads `Assets/MedSimAI/Resources/Scenarios/*.json` (plus `*.ar.json` translations) **server-side** and validates them; you can override the location with `MEDSIM_SCENARIO_DIR`. `GET /health` reports the loaded scenarios, whether a provider key is configured (yes/no only, never the key) and the model names. Logs contain only the route, timing and error messages; they never include request bodies, audio or keys.

Tests (no network, no key): `node --test Backend/medsim-proxy/server.test.mjs`

Before sharing the proxy beyond one PC, put it behind HTTPS (a reverse proxy) and add rate limiting.

## Point the app at it
Create `%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\medsim_ai_config.json`. On Android, use the app's `persistentDataPath` instead.
```json
{ "proxyEndpoint": "http://<pc-ip>:8787", "clientSessionToken": "<same as MEDSIM_CLIENT_TOKEN>", "preferOnline": true, "timeoutMs": 12000, "language": "en" }
```
In VR: **Voice & AI Settings → Live AI via secure proxy**. The status line shows `LIVE AI (validated, scenario-grounded)` or `OFFLINE SCRIPTED DEMO`.

## How grounding works
1. The client sends only: scenario id and version, persona, the student's words, the current stage, the IDs of findings already revealed, a short history, and the language. It never sends clinical facts. Any `allowedFacts` field a client includes is ignored.
2. The proxy asks the model for **Structured Outputs** restricted by JSON-schema enums:
   - Patient: `{factIds: enum(approved fact IDs, max 2), leadIn, offTopic}`.
   - Supervisor: `{intent: enum, investigationId: enum|null, mentionsEscalation, reply}`.
3. **Clinical speech is rendered from approved text.** Patient answers are the authored answers, or the reviewed-draft Arabic translation. Investigation results come only from the scenario engine.
4. Free text, meaning the patient's short lead-in and the supervisor's coaching sentence, is validated on **both** server and client. It must contain no numbers, no diagnosis before the feedback stage, no dosing and no citations, and lead-ins must contain no clinical words. Anything that fails is dropped or replaced with an approved line.
5. If the AI recognised a history question only through intent (no keyword match), the event is tagged `AI_INTENT`. The debrief labels it "recognised via AI intent", so this is never a silent score change.
6. A timeout, network error, version mismatch or rejected output falls back to the deterministic offline mode.

## Voice
- **Speech-to-text:** push-to-talk audio is held in memory, sent to `/v1/stt` and never written to disk. Offline on PC it uses Windows dictation (English).
- **Text-to-speech:** pre-rendered approved English audio is used whenever the line matches exactly, which avoids paid calls. Otherwise `/v1/tts` is used, with distinct patient and supervisor voices.
- **Lip sync:** driven from the audio amplitude plus text-timed Microsoft viseme blend shapes on the MPFB characters.

## Phase 3 additions
- **Palestinian / Levantine Arabic:** the classifier prompt explicitly accepts colloquial Palestinian Arabic (for example «من أيمتى بلش الوجع؟»), and STT receives a vocabulary/dialect prompt. Recognition quality is unverified until a real key is used.
- **Indicators:** a small in-world tag above the addressed character shows *Listening* (red), *Thinking…* (blue) or *Speaking* (teal). It is hidden when idle.
- **Latency:** prompts carry at most 8 history turns, structured classification output is small, and pre-rendered audio is used for exact approved lines, so only novel wording needs TTS. Expected round trip (not measured): STT 0.5–1.5 s + classification 0.5–1 s + TTS 0.5–1.5 s.
- **No billable call has been made.** Starting the proxy with your key and switching to Live AI is the first billable step, and I will ask before doing it.
