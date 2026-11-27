# Security & Privacy

- **No secrets in the client.** No API keys are stored in scripts, Resources, PlayerPrefs or the APK. Provider keys live only in the proxy's environment variables. The client config holds only an endpoint and a proxy-access token, read from `persistentDataPath` at runtime, never from build assets.
- **Microphone.** Recording is push-to-talk only. Audio stays in memory as an `AudioClip` and is destroyed after transcription. It is never written to disk. The reference proxy forwards audio for transcription and doesn't store it; check your provider's data-retention settings.
- **Patient data.** The patient is fictional. The app doesn't collect real patient information. `InputGuard` deflects questions about real people ("my father has chest pain…") with a safety message.
- **Stored data.** `persistentDataPath/medsim_sessions.json` keeps up to 50 session results: scores, timings and event IDs. It holds no audio, no transcripts beyond rubric event details, and no personal identifiers.
- **AI safety in code.** Persona-restricted context, input guard, output validator (fact IDs, numbers, diagnosis disclosure, dosing, state-change claims, citations), and offline fallback. The AI has no write access to case state or scores.
- **Third-party code.** Only official Unity packages and HTC's official VIVE OpenXR plugin are installed. Poly Haven content is data only (textures and FBX).
- **Network.** Android declares the INTERNET permission for Live AI mode. Offline mode makes no network calls.

## Phase 2
- The proxy loads clinical data from disk and ignores any facts the client sends.
- Logs record only the route, timing and error text. No bodies, audio or keys are logged, and `/health` reveals only whether a key is configured.
- Recommended: set `MEDSIM_CLIENT_TOKEN`, put the proxy behind HTTPS, and keep it on the local network for demos.
