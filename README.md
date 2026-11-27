# MedSim AI — Immersive Medical Training Simulator (Prototype v0.1)

> **DRAFT — NOT CLINICALLY VALIDATED.** Technical demonstration with a fictional patient. Not a clinical decision-support tool and not a competency certification.

MedSim AI places a medical student in a VR emergency examination room with a simulated patient (Mr. Adam Reyes, chest pain) and a supervising physician (Dr. Sara Hassan). The student interviews the patient by voice or tablet, examines him with real interactive instruments, requests investigations, explains their reasoning and receives a deterministic, traceable debrief.

## Quick start (PCVR, VIVE Focus 3 via VIVE Business Streaming / SteamVR)
1. Open `D:\Medical-Test\My project` in **Unity 6000.3.29f1**.
2. Open `Assets/MedSimAI/Scenes/MedSim_ER.unity`.
3. Start VIVE Business Streaming (or SteamVR) and connect the headset; make it the active OpenXR runtime.
4. Press **Play**. The menu board floats over the bed; point and pull the trigger on **Start Clinical Session** or **Training Mode**.

No headset? Play still runs the full simulation; use the Unity Test Runner or `AgentScripts/Drive.cs` to drive it. A camera rig fallback sets a 1.65 m eye height when XR is not active.

## Controls
| Action | Input |
|---|---|
| Pick up instrument | Grip |
| Press buttons / UI | Trigger (ray or direct) |
| Push-to-talk | Hold A / X, release to send (keyboard `T` on desktop) |
| Teleport / snap turn | Thumbstick forward / left-right |
| Pause | Left menu button (`Esc` on desktop) |
| Recentre | `R` (desktop) |

You talk to whoever you are looking at (patient or supervisor). The bedside **clinical tablet** has the same questions, investigations, hints, escalation and *Finish case*. This is the main input path in offline mode on Android.

## Clinical session flow
Welcome and controls → interview → bedside measurements (BP cuff on upper arm, pulse oximeter on finger, stethoscope on chest zones) → investigations (12-lead ECG cart, troponin, CXR, glucose) → reasoning and escalation to the supervisor → debrief (objectives, checklist, timing, hints, teaching points, references) → restart and compare.

## Rebuilding generated content
- `MedSim AI ▸ Build ER Scene` regenerates the scene, meshes and materials (`Assets/MedSimAI/Generated`).
- `MedSim AI ▸ Generate Branding` regenerates the icon and splash.
- `MedSim AI ▸ Configure Platforms (PCVR + Android)` re-applies player and quality settings.
- If you change scenario text, re-render the offline voices: export `AgentScripts/VoiceManifest.cs` (via `unity command script eval`), then run `AgentScripts/RenderVoices.ps1`. `VoiceCoverageTests` fails if a line is missing audio.

## Documentation
[ARCHITECTURE](ARCHITECTURE.md) · [CLINICAL_SCENARIO](CLINICAL_SCENARIO.md) · [CLINICAL_REVIEW](CLINICAL_REVIEW.md) · [MEDICAL_REFERENCES](MEDICAL_REFERENCES.md) · [ASSET_CREDITS](ASSET_CREDITS.md) · [AI_SETUP](AI_SETUP.md) · [SECURITY_AND_PRIVACY](SECURITY_AND_PRIVACY.md) · [VR_SETUP](VR_SETUP.md) · [TEST_REPORT](TEST_REPORT.md) · [KNOWN_LIMITATIONS](KNOWN_LIMITATIONS.md)

## Phase 2 (2026-11-26)
- **Realistic characters.** Dr. Sara Hassan and Mr. Adam Reyes are now MakeHuman/MPFB2 humans: textured, humanoid-rigged (53 bones), with 74 facial blend shapes. They breathe, blink, follow you with eyes and head, show expressions, gesture, and lip-sync with visemes. The patient reclines on the bed and the examination zones are fitted to his body surface.
- **Lighting.** Baked GI and ambient occlusion with light probes (`MedSim AI ▸ Bake Lighting`), plus a patient chart on the footboard and a doctor-led onboarding.
- **Instruments.** Faint placement guidance on every valid zone while an instrument is held, eased snapping, pickup/confirm sounds, a BP pump sound, and haptics.
- **Live AI.** The proxy now loads the scenario server-side and uses structured outputs. The model only picks approved fact IDs and intents; all clinical speech is approved text. See [AI_SETUP](AI_SETUP.md). Offline mode remains the default.
- **Arabic (optional, draft).** A separate reviewable translation file, shaped RTL subtitles, and Arabic speech through Live AI.
- **Regenerating characters.** Run `Assets/MedSimAI/Characters/make_character_mpfb.py.txt` with the portable Blender at `D:\Medical-Test\Tools`, then `MedSim AI ▸ Setup Realistic Characters`, then `Build ER Scene`, then `Bake Lighting`.
