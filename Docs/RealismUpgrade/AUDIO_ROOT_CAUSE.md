# Audio and voice: root causes and fixes

> **Draft — not clinically validated.** Applies to the integrated hospital on `integration/surgery-final`.

## What was reported
- The doctor could not hear the student, and the student could not hear the doctor or the patient.
- Characters seemed to respond (text appeared), but no speech was audible.
- The student's own words no longer appeared in the conversation panel.

## Evidence examined
- **Player log of the manual session** (`%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\Player.log`, 2026-11-28 03:27–03:32):
  - The executable that ran was `D:/VRBuilds/MedSim_Desktop`, the **v0.6 build**, not the integrated build.
  - Every request failed with `Failed to connect to 127.0.0.1:8787`.
- **Proxy ledger:** no request at all since 00:34 on 2026-11-28. Nothing reached the AI service during that session.
- **Proxy copies:** the proxy normally started from `D:\Medical-Test\My project\Backend\medsim-proxy` comes from a branch **without surgical routes**. `/v1/surgery/*` does not exist there.

## Root causes
| # | Cause | Effect | Fix |
|---|---|---|---|
| 1 | The AI proxy was not running during the manual session. Nothing starts it automatically, and v0.6 has always needed `node server.mjs` first. | Every character fell back to text only. No voice in or out. | One-click launchers in `D:\Medical-Test\`: `Start_MedSim_AI_Desktop.cmd` and `Start_MedSim_AI_PCVR.cmd` start the proxy if it is not running, then the app. `Start_MedSim_Proxy_Integrated.cmd` starts the proxy alone. |
| 2 | The usual proxy folder has no surgical routes. | Room 02 voice would fail even with that proxy running. | The launchers start the **integrated** proxy. If an outdated proxy is running, Room 02 now says so ("…no surgical routes… start the integrated proxy"). |
| 3 | Each proxy checkout kept its own ledger (`data/usage.jsonl`). | Starting the integrated proxy would have counted from $0 and could exceed the $2 authorisation. | `MEDSIM_LEDGER_FILE` gives every checkout the same ledger. The launchers set it to the existing ledger, and the start-up line prints the ledger path and the amount spent. |
| 4 | **Race when leaving Room 01.** The live session was ended asynchronously while its id was still set. | The doctor's next line used the session being ended, was refused, and fell back to text only. | The id is now cleared before the end request, so the next line opens a fresh session. |
| 5 | **Wrong listener.** The voice target was chosen by gaze only. | From the corridor the student could address Mr. Reyes behind a wall. | In the hospital the patient is addressable only inside Room 01. |
| 6 | **Room 02 transcript.** The surgical voice path wrote the student's words only into the surgical HUD transcript, which is hidden in the hospital so panels do not overlap. | The student's words never appeared in the operating room. | The surgical voice path now reports what it heard to the shared conversation panel. |
| 7 | **Surgical reply language.** The reply language did not follow the student's spoken language (the branch was empty). | An English request after Arabic speech got an Arabic reply. | With no explicit language choice, it now follows the student's spoken language. |

Not causes (checked):
- **AudioSource set-up:** the doctor and patient voices are 2D at volume 1; Sami's is 3D (max 12 m); one AudioListener on the camera; no mixer, mute or pause.
- **Streaming, decoding and lip sync:** measured working in the executable. See [TEST_REPORT](TEST_REPORT.md).

## Verification
| Test | What is real | What is synthetic | Result |
|---|---|---|---|
| `MedSimAI.exe -hospitalVoiceTest` with `tools/fake-provider-server.mjs` | The executable, the T key through the Input System, the **real microphone**, proxy routes, streaming, AudioSource, lip sync, hospital routing, Room 02 voice, the hand-over | Speech recognition (transcript from a queue) and the voice waveform (no key, no network, temporary ledger) | Speech audible in all **9/9** probes (reception, corridor, Room 01 doctor and patient, Room 02 surgeon and assistant). Microphone captured 1.4–1.9 s per push-to-talk. 0 exceptions. Passed from the **original build folders** for both Desktop and PCVR (desktop fallback). |
| `MedSimAI.exe -hospitalLiveSmoke` (**paid**) | Everything, including the OpenAI Realtime service. Clips were played through the speakers into the **real microphone**. | The student's voice (pre-generated synthetic clips) | The live welcome was audible. "Hello Dr. Hassan, nice to meet you." was heard exactly and answered aloud. «ارفع إيدك الشمال لو سمحت» was heard exactly; Mr. Reyes answered aloud in Arabic **and raised his left arm**. Cost $0.0234. |

Recording of the app's own output from the live run (no microphone audio): `Screenshots/Surgery/RealismUpgrade/audio_live/live_app_output_mono24k.wav`.

**Not done:**
- a test with a **human** speaking;
- a test on the **VIVE Focus 3** (headset audio device and microphone);
- a live paid test of Room 02.

Those remain for the owner. See [KNOWN_LIMITATIONS](KNOWN_LIMITATIONS.md).
