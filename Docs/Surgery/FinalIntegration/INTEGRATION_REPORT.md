# Final integration report: MedSim AI training hospital

> **DRAFT — NOT CLINICALLY VALIDATED.** Both training scenarios are educational simulations for a student project. They are not a substitute for clinical supervision, and their scores are not a competency certification. A qualified clinician must review the clinical content before any real training use (see [CLINICAL_REVIEW.md](CLINICAL_REVIEW.md)).

Date: 2026-11-28 · Unity 6000.3.29f1 · branch **`integration/surgery-final`** (based on `feature/surgery-complete` @ `e4fbd9c`).

## What was integrated
The four contributions are combined into one application: a **training hospital** with a reception lobby, a corridor, **Room 01 (clinical examination)** and **Room 02 (surgical training)**. One **persistent doctor**, Dr. Sara Hassan, welcomes the student and guides them between the rooms.

| Person | Contribution | Branch / PR | In this candidate |
|---|---|---|---|
| 1 | Operating room, equipment, robot cart, anchors, entry event, navigation | `feature/surgery-environment`, PR #1 (merged earlier), PR #3 (draft) | Merged locally (`7824e0c`). The room is placed on the hospital corridor; its generator was adjusted for the real knee site. |
| 2 | Surgical patient, knee anatomy (44 structures, 7 targets, 9 states), implants, 13 instruments, tray, drapes, catalogs | `feature/surgery-anatomy-assets`, PR #5 | Merged locally (`b800482`). Contract tags are now serialized natively, and the drape fit is fixed. |
| 3 | Scenario engine, instruments, anatomy interaction, implant placement, robot, reset, desktop adapter | `feature/surgery-mechanics`, PR #4 | Merged locally (`403408a`). It now runs on 6000.3.29f1, follows the authored multi-step objectives, and drives the real anatomy. **No mock mechanics in the hospital.** |
| 4 | Contract, AI surgeon/assistant, supervisor, hand-over, assessment, debrief, HUD, proxy routes | `feature/surgery-ai-integration`, PR #2 | Merged locally (`dfc2664`). The surgeon role is now played by the persistent doctor, and the HUD is unified with the clinical conversation panel. |

The commit list is in [MERGE_HISTORY.md](MERGE_HISTORY.md) and the per-PR findings are in [PR_REVIEW_REPORT.md](PR_REVIEW_REPORT.md).

## Integration problems found and fixed
| # | Problem | Fix | Commit |
|---|---|---|---|
| 1 | `SurgerySceneTags.cs` held five MonoBehaviours. Unity saved only `SurgeryAnchor`. The `SurgeryEventBusHost` in Person 3's prefab and test scenes deserialized as a `SurgeryAnchor`, so those scenes had **no event bus**. | One file per class. The anchor keeps its GUID. The three serialized hosts were migrated. `Instance` resolves lazily. | `751cb58` |
| 2 | 5 of Person 3's 12 tests failed on 6000.3.29f1. Dependencies were wired only in `Awake`, and `??` was used on Unity fake-null. | Lazy wiring and explicit null checks. The original tests now pass unchanged. | `8c34501` |
| 3 | Mechanics completed an objective on its **first** valid contact: the cutting guide alone "resected" the femur, and one trial completed the trial reduction. | Authored sequencing: guide → powered saw; every component seated → impaction; tray before insert; both trials. 4 new tests walk all 9 objectives. | `8c34501` |
| 4 | Implant reset stacked every implant on one point. Instrument reset lost the parent and kinematic state. The robot controller moved any object it sat on. | Implants return to their own start poses and instruments to their tray slots. The robot moves only a bound robot. | `8c34501` |
| 5 | Person 2's bridge components were needed because of #1. | Native serialized tags in all prefabs. The bridges were removed. Tests check there is exactly one tag, one grab interactable and one rigidbody per prefab. | `b1a2f24` |
| 6 | **Surgical site 0.39 m off.** The room's beams, student position and assistant home assumed a straight leg. The robot was 2.2 m from the knee, beyond the arm's 1.45 m reach. | Person 1's generator aims everything at the flexed-knee anchor. The robot is docked at (1.05, 0, −0.75). | `c483ded` |
| 7 | **Tray overlap.** Placeholder trays on the back table. | Placeholders removed. The real tray is on the Mayo stand next to the student. `ANCHOR_ImplantTable` moved to the back table. | `c483ded` |
| 8 | **Toes through the drape** (reported earlier as "fingertips") and the gown showing through it. | A fitted drape copy: 1 cm clearance plus a tent over the left toes. The 2 cm body–pad contact is hidden by the drape and kept as pad compression. | `4e8857b` |
| 9 | `SurgerySessionCore.Restart` reset the mechanics **before** the new bus session, so objective 1's `objective.started` was lost. The controller could pick the bare engine instead of the mechanics root, depending on scene order. | Reset after `BeginSession`. The controller prefers `SurgeryMechanicsRoot`. | `74a1c6a`, `d5019a5` |
| 10 | Two conversation UIs overlapped in the operating room. The HUD transcript rendered Arabic unshaped. Glyphs showed as boxes. Sami was labelled as the supervisor. The surgical HUD stayed on screen in the corridor and Room 01. | One conversation panel. Glyph mapping. Per-speaker Arabic names. The HUD canvas follows the component. | `a7ee5b4`, `9ed089c` |
| 11 | The OR assistant reused the **Room 01 patient's** body (same face, gown and wristband). The procedural cap and mask were misplaced because the attire matched "eye" inside "Reyes". | A distinct Rocketbox scrub character (`Medical_Male_04`, MIT). The attire fixes are kept for other models. | `a7ee5b4`, `1cef175` |
| 12 | Duplicate doctor persona ("Dr. Lina Haddad") in PR #2. | The surgeon persona is Dr. Sara Hassan, same voice: one persistent doctor. | `d5019a5` |
| 13 | XR duplication risk (§7.8). | Verified: Person 3 subscribes to Person 2's single `XRGrabInteractable` and adds none. The hand-over no longer re-parents the instrument to the assistant's hand. | `74a1c6a` |
| 14 | `inst.forceps` and `inst.suction` (§7.6). | Kept as **non-essential educational assets**. They are not in the contract or the scenario, and no objective uses them. | — |

There were **no textual merge conflicts**. All five merges were clean. The problems above were behavioural and were found by compiling, testing and running the combined project.

## The hospital (new)
- **Scene:** `Assets/MedSimAI/Hospital/Scenes/MedSim_Hospital.unity`, generated by **MedSim AI → Hospital → Build Hospital Scene** from a *copy* of `MedSim_ER.unity`. The original clinical scene is unchanged and still in the build. Launch it on its own with `MedSimAI.exe -clinical-only`.
- **Assembly:** `MedSimAI.Hospital`. It references the clinical and surgical assemblies; nothing references it, so there are no cycles.
- Architecture: [HOSPITAL_ARCHITECTURE.md](HOSPITAL_ARCHITECTURE.md). Journey: [COMPLETE_SCENARIO_FLOW.md](COMPLETE_SCENARIO_FLOW.md). Doctor and AI: [AI_GUIDE_ARCHITECTURE.md](AI_GUIDE_ARCHITECTURE.md).

## Status by verification level
| Level | What |
|---|---|
| **Integrated and tested in the Unity Test Runner (offline)** | Hospital welcome; guided walk with no teleporting; room-entry sessions (also after a teleport); leaving a room ends its session; session isolation; the full 9-objective surgical run with rejected attempts and assessment; verified hand-over; assessment vs. guided mode; language continuity; truthful no-path reporting; all clinical (v0.6) regression tests |
| **Integrated and run in the standalone Desktop executable** | The whole journey above, plus Room 01 with a patient action, with screenshots and timings (`-hospitalSelfTest`). See [TEST_REPORT.md](TEST_REPORT.md). |
| **Mock-tested** | Proxy routes (fake AI): surgical routes, grounding, language. Live AI was **not** used (budget, see below). |
| **Built, not run on a headset** | PCVR executable. It starts and completes the journey in its Desktop fallback, but it was **not** tested on a VIVE Focus 3. |
| **Not tested** | Real headset, real human voice through the hospital flow, live AI in the hospital, multiplayer. |
| **Clinically unverified** | All surgical sequencing, tolerances, implant geometry, resection planes, pin positions, opening size, and the clinical case content. |

## Builds
| Build | Path | Result |
|---|---|---|
| Desktop (OpenXR removed for this build only) | `D:/VRBuilds/MedSimAI_Integrated_Desktop/MedSimAI.exe` | Succeeded, 405 MB. Full self-test journey passed. |
| PCVR (OpenXR included) | `D:/Medical-Test/Builds/MedSimAI_Integrated_PCVR/MedSimAI.exe` | Succeeded, 408 MB. Starts and completes the journey in its Desktop fallback. **No headset test.** |

The v0.6 builds were not touched, no Android APK was built, and no executable is in Git.

## API budget
- **No live AI calls were made by this integration.** Every Unity test and exe run forced offline mode, and the proxy was stopped before the Play-mode work.
- The ledger shows **$1.9204 spent of the $2.00 authorisation ($0.0796 remaining)**. The earlier report said about $0.095.
- The difference is **≈ $0.0158**: one session start and two live welcome lines (`say-stream`, Instructor) recorded at 00:33–00:34 local time on 2026-11-28, while the proxy from the previous session was still running.
  - That is **before the first integration merge (00:56)** and before any hospital code existed. The pattern matches a start of the v0.6 clinical scene (Play mode or the v0.6 exe).
  - Which client made the calls could not be identified from the editor logs.
  - No live calls have been recorded since the proxy was stopped.
- The budget was not reset or increased.

## Incident: uncommitted font changes lost
At the start there were three uncommitted changes in the main working folder (`D:/Medical-Test/My project`):
- `ProjectSettings/ProjectSettings.asset`;
- two TMP font assets (`NotoKufiArabic SDF`, `NotoSansArabic SDF`).

Before the integration worktree was set up, the editor that had the main project open was closed through a script that called `AssetDatabase.SaveAssets()`. **That save wrote the in-memory font assets over the uncommitted font files.** The two TMP font changes are gone: they now match the committed version, and they had not been backed up. This was a mistake in the integration procedure.

| Item | State |
|---|---|
| `ProjectSettings.asset` change | Still uncommitted in the main folder, and backed up to `D:/Medical-Test/original_uncommitted_changes_backup.patch` |
| Integration commits | Include none of these files |
| The two TMP font changes | **Lost** |

If the font changes are still needed, they must be redone. For example, regenerate the SDF assets with the settings that were intended.

## GitHub
See [MERGE_HISTORY.md](MERGE_HISTORY.md#github-delivery):
- The integration branch is pushed.
- A pull request into `master` is open for the owner's review and **not merged**.
- The contributor PRs (#2, #3 draft, #4, #5) were **not merged remotely**: none has an approving review, and #3 is still a draft.
