# MedSim AI — Immersive Medical Training Simulator (Prototype v0.2)

> **DRAFT — NOT CLINICALLY VALIDATED.** Educational technical demonstration with a fictional patient. Not for real-patient diagnosis or treatment, and not a competency certification.

The experience runs as one continuous scene with no loading screens:

1. **Corridor.** The student starts in a hospital corridor beside the supervising physician, Dr. Sara Hassan, who gives a short welcome generated live by AI. The provisional bilingual sign reads «جامعة بيرزيت — أهلاً وسهلاً» / *Birzeit University — Welcome*.
2. **Door.** The student opens the examination-room door by its lever handle, and Dr. Hassan walks in with them.
3. **Assessment.** The patient, Mr. Adam Reyes (58, chest pain), is resting in a hospital bed. The student talks to him naturally by voice in English or Arabic and examines him with real instruments. Both characters answer with AI-generated, scenario-grounded speech.
4. **Supervision.** Dr. Hassan observes silently and steps in only when the scenario rules call for it.
5. **Patient condition.** It evolves through authored states.
6. **Discussion and debrief.** The session ends with a clinical discussion and a personalised spoken debrief.

## Quick start (PCVR, VIVE Focus 3 via VIVE Business Streaming / SteamVR)
1. **Start the AI proxy** (one time per session):
   ```
   cd "D:\Medical-Test\My project\Backend\medsim-proxy"
   node server.mjs
   ```
   Secrets are read from `D:\Medical-Test\secrets\medsim.env` (see [AI_SETUP.md](AI_SETUP.md)). The Unity client config is created once with `node tools/write-unity-config.mjs`.
2. **Connect the headset** and make it the active OpenXR runtime.
3. **Launch** `D:\Medical-Test\Builds\MedSimAI_PCVR_v0.2\MedSimAI.exe`, or open `Assets/MedSimAI/Scenes/MedSim_ER.unity` in Unity 6000.3.29f1 and press Play.

Without the proxy the app still runs, **text-only** (subtitles), and says so. It never plays pre-recorded dialogue.

## Controls
| Action | Input |
|---|---|
| Talk to the person you are looking at | Hold A / X, speak, release (keyboard `T` on desktop) |
| Open the door | Grab the lever handle (grip), push the door open |
| Pick up and apply instruments | Grip; release on the patient (cuff → upper arm, oximeter → fingertip, stethoscope → hold on the chest) |
| Record ECG | Press the RECORD button on the ECG cart |
| Pause menu (tablet on/off, subtitles, language EN/AR, comfort and height, restart) | Left menu button (`Esc` on desktop) |
| Teleport / snap turn | Thumbstick |

## Documentation
- **Design:** [RESEARCH_AND_VISUAL_DIRECTION](RESEARCH_AND_VISUAL_DIRECTION.md) · [ARCHITECTURE](ARCHITECTURE.md) · [INTELLIGENT_SUPERVISOR](INTELLIGENT_SUPERVISOR.md) · [DYNAMIC_PATIENT_STATE](DYNAMIC_PATIENT_STATE.md)
- **AI and security:** [AI_SETUP](AI_SETUP.md) · [SECURITY_AND_PRIVACY](SECURITY_AND_PRIVACY.md)
- **Clinical:** [CLINICAL_SCENARIO](CLINICAL_SCENARIO.md) · [CLINICAL_REVIEW](CLINICAL_REVIEW.md) · [MEDICAL_REFERENCES](MEDICAL_REFERENCES.md)
- **Testing:** [LIVE_AI_TEST_REPORT](LIVE_AI_TEST_REPORT.md) · [TEST_REPORT](TEST_REPORT.md) · [KNOWN_LIMITATIONS](KNOWN_LIMITATIONS.md)
- **Credits:** [ASSET_CREDITS](ASSET_CREDITS.md)
- **Earlier audit:** [MEDSIM_REALISM_AND_EXPERIENCE_AUDIT](MEDSIM_REALISM_AND_EXPERIENCE_AUDIT.md)

## Rebuilding generated content (Unity menu *MedSim AI*)
Run these in order:
1. *Build ER Scene*
2. *Build Redesign (v0.2)*
3. *Characters v2*
4. *Bake Lighting*

Re-running only *Build ER Scene* restores the old MakeHuman characters.

## Restore points (git)
| Commit | State |
|---|---|
| `78ed12c` | Before the redesign (post-audit) |
| `a7ad1d4` | Live AI + supervisor |
| `ab3c13f` | Environment |
| `054cbc6` | Characters |
| `75c6a05` | Live end-to-end fixes |
