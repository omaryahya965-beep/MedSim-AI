# MedSim AI — Surgical Training Module: Integration (Person 4)

> **DRAFT — NOT CLINICALLY VALIDATED.** Educational simulation for a student project. Not a substitute for clinical supervision or accredited surgical training. Scores are not a competency certification.

This folder documents the AI surgeon, surgical assistant, assessment, scenario menu and the integration of the four contributions. The shared rules (ids, events, anchors, ownership, merge policy) are in [`../SURGERY_CONTRACT.md`](../SURGERY_CONTRACT.md).

| Document | Contents |
|---|---|
| [INTEGRATION_REPORT.md](INTEGRATION_REPORT.md) | What is integrated, per person, with commit hashes, and what is still missing |
| [AI_ARCHITECTURE.md](AI_ARCHITECTURE.md) | Supervisor, characters, action registry, voice, proxy routes, safety |
| [ASSESSMENT_SYSTEM.md](ASSESSMENT_SYSTEM.md) | Deterministic scoring rules and the debrief |
| [TEST_REPORT.md](TEST_REPORT.md) | Actual test results and how to run them |
| [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) | Honest list of gaps |
| [CLINICAL_REVIEW.md](CLINICAL_REVIEW.md) | Review status and the reviewer checklist |

## Quick start (Unity 6000.3.29f1)

1. Open the project. Run **MedSim AI → Surgery → Build All Surgery Scenes**. It builds `ScenarioSelect.unity`, `SurgeryAI_Test.unity` and `OperatingRoom.unity`, puts the menu first in the build settings, and writes an integration report to `Docs/Surgery/Integration/LAST_SCENE_BUILD.txt` that lists every contract prefab found or missing.
2. Start the AI proxy as usual (`cd Backend/medsim-proxy && node server.mjs`). The surgical routes (`/v1/surgery/*`) are on the same proxy, with the same key, client token and budget ledger. Without the proxy the module runs **text-only** and says so.
3. Press Play in `ScenarioSelect`. Choose **Surgical Training**.

**In the operating room**
- Talk: hold **T** (desktop) or **A/X** (VR); **M** toggles hands-free. Speaking interrupts a character.
- **H** = ask for help, **J** = explain the current step (these also work without a microphone).
- **Esc** / left menu button = pause menu (restart, language, microphone, finish, return to menu).
- **F10** / hold left menu button for 2 s = back to the scenario menu, from either simulation.
- While Person 3's mechanics are not merged, the scene uses **mock mechanics** (labelled on screen and in the debrief, not scored as real): **N** complete step · **W** wrong tool · **S** wrong side · **P** out of order · **O** out of tolerance · **G** take the instrument the assistant is offering.

## Testing without Unity

```bash
Tools/OffUnityTests/run-all.sh      # needs .NET 8 SDK + Node 22; no API key, no network, no spending
```
