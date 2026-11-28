# MedSim AI — Immersive Medical Training Simulator (Prototype v0.2)

> **DRAFT — NOT CLINICALLY VALIDATED.** Educational technical demonstration with a fictional patient. Not for real-patient diagnosis or treatment, and not a competency certification.

## Integrated training hospital (branch `integration/surgery-final`)
The four team contributions are combined into one **training hospital**:
- reception lobby and corridor;
- **Room 01 · Clinical Examination** (the v0.6 chest-pain case below, unchanged);
- **Room 02 · Surgical Training** (draft total knee arthroplasty: Person 1's operating room, Person 2's patient, anatomy and instruments, Person 3's mechanics, Person 4's AI surgeon, assistant and assessment).

One persistent doctor, Dr. Sara Hassan, welcomes the student. She walks with them to either room when asked by voice («خديني على غرفة العمليات», "take me to room one"), by the lobby directory or by keys 1 / 2 / 3.

| | |
|---|---|
| Scene | `Assets/MedSimAI/Hospital/Scenes/MedSim_Hospital.unity` (menu *MedSim AI → Hospital → Build Hospital Scene*) |
| **Start (voice included)** | `D:\Medical-Test\Start_MedSim_AI_Desktop.cmd` or `Start_MedSim_AI_PCVR.cmd`. These start the AI proxy first (without it the characters are text only), then the app. |
| Desktop build | `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe` (add `-clinical-only` for the v0.6 scene alone; v0.6 backup: `D:\VRBuilds\Backup_v0.6_MedSim_Desktop_20261128`) |
| PCVR build | `D:\Medical-Test\Builds\MedSimAI_PCVR_v0.2\MedSimAI.exe` (**not yet tested on a headset**; v0.6 backup: `…\Backup_v0.6_MedSimAI_PCVR_v0.2_20261128`) |
| AI proxy | `D:\Medical-Test\Start_MedSim_Proxy_Integrated.cmd`: the integrated proxy (clinical + surgical routes) on the shared spending ledger |
| Controls | [DESKTOP_CONTROLS](Docs/Surgery/FinalIntegration/DESKTOP_CONTROLS.md). New: after applying the cuff, press **E** on the NIBP monitor to START the blood-pressure measurement. |
| Report | [INTEGRATION_REPORT](Docs/Surgery/FinalIntegration/INTEGRATION_REPORT.md) · realism upgrade, increment 1: [IMPLEMENTATION_REPORT](Docs/RealismUpgrade/IMPLEMENTATION_REPORT.md) · [AUDIO_ROOT_CAUSE](Docs/RealismUpgrade/AUDIO_ROOT_CAUSE.md) · [TEST_REPORT](Docs/RealismUpgrade/TEST_REPORT.md) · [KNOWN_LIMITATIONS](Docs/RealismUpgrade/KNOWN_LIMITATIONS.md) |

Further documents:
- **What was done:** [PR_REVIEW_REPORT](Docs/Surgery/FinalIntegration/PR_REVIEW_REPORT.md) · [MERGE_HISTORY](Docs/Surgery/FinalIntegration/MERGE_HISTORY.md)
- **How it works:** [HOSPITAL_ARCHITECTURE](Docs/Surgery/FinalIntegration/HOSPITAL_ARCHITECTURE.md) · [COMPLETE_SCENARIO_FLOW](Docs/Surgery/FinalIntegration/COMPLETE_SCENARIO_FLOW.md) · [AI_GUIDE_ARCHITECTURE](Docs/Surgery/FinalIntegration/AI_GUIDE_ARCHITECTURE.md)
- **Status:** [TEST_REPORT](Docs/Surgery/FinalIntegration/TEST_REPORT.md) · [PERFORMANCE_REPORT](Docs/Surgery/FinalIntegration/PERFORMANCE_REPORT.md) · [KNOWN_LIMITATIONS](Docs/Surgery/FinalIntegration/KNOWN_LIMITATIONS.md) · [CLINICAL_REVIEW](Docs/Surgery/FinalIntegration/CLINICAL_REVIEW.md)
- **Licences and VR testing:** [ASSET_CREDITS](Docs/Surgery/FinalIntegration/ASSET_CREDITS.md) · [VR_MANUAL_TEST_CHECKLIST](Docs/Surgery/FinalIntegration/VR_MANUAL_TEST_CHECKLIST.md)
- **Licence texts:** [Licenses/](Licenses/)

Both scenarios are **DRAFT — NOT CLINICALLY VALIDATED**.

## Clinical scenario (v0.6, Room 01)
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

## Desktop Mode (no headset)
Run `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe` (start the AI proxy first). Controls: WASD + mouse, E to interact, left click to grab/apply, right click to put back, hold T to talk, Esc to pause. See [DESKTOP_MODE](DESKTOP_MODE.md).

## Controls (VR)
| Action | Input |
|---|---|
| Talk to the person you are looking at | Hold A / X, speak, release (keyboard `T` on desktop) |
| Open the door | Grab the lever handle (grip), push the door open |
| Pick up and apply instruments | Grip; release on the patient (cuff → upper arm, oximeter → fingertip, stethoscope → hold on the chest) |
| Record ECG | Press the RECORD button on the ECG cart |
| Pause menu (tablet on/off, subtitles, language EN/AR, comfort and height, restart) | Left menu button (`Esc` on desktop) |
| Teleport / snap turn | Thumbstick |

## v0.6: Embodied intelligence
Give physical instructions in Palestinian Arabic, MSA, English or a mix, several at once if you like: «اقعد شوي وارفع إيدك الشمال», «قرّب إيدك علي», «ريّح إيدك», «دكتورة، تعالي من الجهة الثانية», «ممكن تتركيني أفحص المريض لحالي؟».
- Every action is validated, queued and tracked (Requested → InProgress → Completed / Interrupted / Rejected …), and the real result goes back to the AI.
- Poses are held until you say otherwise, and instruments ride on the moving arm.
- Standing and walking are always declined.
- The characters look at whoever is speaking.

See **EMBODIED_AI.md** for evidence GIFs, exe performance and limitations. Recovery point: git tag `v0.5-stable`, plus builds in `D:/VRBuilds/Backup_v0.5_c1b73d6`.

## v0.5: Conversation panel and embodied AI
The conversation panel never overlaps or overflows: long replies page in step with the speech, and Arabic is right-to-left. Ask the patient to sit up, raise or extend an arm, breathe deeply, open his mouth or show where it hurts, and ask the doctor to come, step back or check the patient. They do it, within the scenario's safety policy. See **EMBODIED_AI_AND_CONVERSATION_UI.md**. Recovery point: git tag `v0.4-stable`, plus builds in `D:/VRBuilds/Backup_v0.4_9ccc8f6`.

## v0.4: Arabic, complete speech, VR movement
Speak Arabic or English: both characters follow you. Say «احكي عربي» or "speak English" to fix the language for both. Lines now play to the very end. VR: left stick moves, right stick turns or teleports, and the pause menu adjusts height (default +55 cm). See **ARABIC_AUDIO_VR.md**, including the manual checklist.

## Native voice (v0.3)
Speak naturally: hold **T** on desktop or **A/X** in VR (press **M** for hands-free). Replies stream back in about 2 s, and you can interrupt. Subtitles and transcripts are unchanged. See **NATIVE_VOICE.md**, including the microphone checklist.

## Documentation
- **Desktop Mode:** [DESKTOP_MODE](DESKTOP_MODE.md)
- **Embodied AI (v0.6):** [EMBODIED_AI](EMBODIED_AI.md) · [EMBODIED_AI_AND_CONVERSATION_UI](EMBODIED_AI_AND_CONVERSATION_UI.md)
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
5. *Polish/3. Fabric and skin micro-detail* (v0.6 gown weave and skin pores)

Re-running only *Build ER Scene* restores the old MakeHuman characters.

## Restore points (git)
| Commit | State |
|---|---|
| `78ed12c` | Before the redesign (post-audit) |
| `a7ad1d4` | Live AI + supervisor |
| `ab3c13f` | Environment |
| `054cbc6` | Characters |
| `75c6a05` | Live end-to-end fixes |
