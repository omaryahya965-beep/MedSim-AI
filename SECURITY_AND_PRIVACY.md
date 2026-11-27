# Security and Privacy (v0.2)

## Provider credentials
- **Where the key lives.** The OpenAI key exists only on the proxy machine:
  - preferred: the `OPENAI_API_KEY` environment variable;
  - otherwise: `D:\Medical-Test\secrets\medsim.env`, outside the Unity project and the git repository, readable only by the owning Windows user.
  - It is never in Unity scripts, Resources, scenes, builds, client config, logs, screenshots or docs.
- **What the client holds.** Unity holds only `MEDSIM_CLIENT_TOKEN`, a shared secret that authorises the client to the proxy. It is not a provider key, is stored in `%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\medsim_ai_config.json`, and can be rotated at any time.
- **Repository protection.** `.gitignore` excludes `.env`, `*.env`, `secrets/`, `medsim_ai_config.json`, `Backend/medsim-proxy/data/` and `AssetStaging/`.
- **Log redaction.** Proxy logs record the route, timing and an error class only. Request bodies, audio and keys are never logged. All error text passes through `redact()`, which masks `sk-…` and `Bearer …` strings; this is unit-tested.
- **No key in builds.** Verified by searching the built player for key material (see TEST_REPORT.md).
- **Exposed key.** The key supplied for this project was pasted into a chat transcript. **Rotate it after testing.**

## Network
- **Bind address.** The proxy binds to `127.0.0.1` by default. Exposing it to a standalone headset (`MEDSIM_HOST=0.0.0.0`) requires HTTPS (reverse proxy) and firewall rules.
- **Authentication.** Every API route except `/health` requires the client token. Without `MEDSIM_CLIENT_TOKEN`, every route returns 401.
- **Spend and abuse controls:**
  - hard budget cap (in-app, default $2.00);
  - 40 requests per minute;
  - 160 turns per session;
  - body size limit 4 MB;
  - audio up to 30 s;
  - text length limits.

## Student data
- **Microphone audio.** Held in memory, sent to the proxy, and forwarded to OpenAI for transcription. It is **not written to disk** by Unity or the proxy. Requests use `store: false` where the API supports it. Please review OpenAI's data-retention terms for your organisation.
- **Transcripts and history.** Conversation history lives in proxy memory for the session only (deleted on session end or after 3 h).
- **Session results.** Saved locally (`medsim_sessions.json` in persistentDataPath): scores and event ids only. No audio and no names.
- **Fictional patient only.** The app refuses requests for real-patient advice (`InputGuard`).

## Medical safety
See [ARCHITECTURE.md](ARCHITECTURE.md) (grounding rules) and [CLINICAL_REVIEW.md](CLINICAL_REVIEW.md). **DRAFT — NOT CLINICALLY VALIDATED.** This is an educational technical demonstration, not for real-patient diagnosis or treatment.
