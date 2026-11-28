# Known limitations

These limitations apply to the integrated hospital build. They are stated plainly so nothing is mistaken for a finished, validated feature.

## Clinical validity
- **DRAFT — NOT CLINICALLY VALIDATED.**
  - The surgical sequence, tolerances, resection planes, implant geometry, guide positions and incision size are illustrative.
  - The clinical case (ER chest pain) is the v0.6 draft.
  - Nothing here has been reviewed by an orthopaedic surgeon or an emergency physician. See [CLINICAL_REVIEW](CLINICAL_REVIEW.md).
- Scores are formative feedback for a student project, **not a competency assessment**.

## Not tested
- **No VIVE Focus 3 (or any headset) test.** The PCVR build exists and starts. VR grab, hand-over in VR, contact triggers, comfort and headset frame rate are unverified.
- **No real human voice test** in the hospital build. Exe voice input was injected text, and speech output was the offline subtitle mode.
- **No live AI test in the hospital.** All guide, supervisor and assistant behaviour was verified with offline or fake providers. Live wording quality in the hospital context is unknown.
- **Not run:** the off-Unity C# harness from PR #2 (.NET 8 targeting pack unavailable offline) and the GitHub Actions workflow.
- **No dedicated tests** for changing room while a character is walking, for repeated restarts within one exe run, or for interrupting speech inside the hospital.

## Simulation fidelity
- **Tissue changes are discrete state swaps** (intact → marked → incised → resected → implanted…). There is no cutting simulation, deformation, bleeding or bone dust.
- **Contact rule:** any collider of the tool entering the target trigger counts, not the blade or the tip.
  - Desktop uses click-to-apply.
  - In VR, an accidental brush can register as a rejected attempt and lower the score.
- **The robot is a visual guidance aid.** It moves its arm over the resection target (CCD IK) but does not cut, constrain the saw or give haptics. Its arm carries no contract `robot.arm` tag; it is bound by joint names.
- **Hand-over:**
  - Sami walks to the back table and holds the instrument out, and the hand-over completes on the student's grab.
  - There is **no hand-reach animation** or finger pose: the instrument is held at the hand socket.
- **One fixed knee flexion pose.** The leg cannot be moved, and there are no knee LODs.
- `inst.forceps` and `inst.suction` exist as **non-essential educational assets**. They are not part of the contract and no objective uses them.
- `MockMechanicsDriver` still exists for Person 4's standalone test scene. It is disabled in the hospital, and a test asserts there are no mock events.

## Hospital
- Two rooms only. The lobby, corridor extension and directory are generated, not artist-made.
- Session records last only while the app is running. There is no saved history.
- One ~100 ms frame hitch in the corridor when the guided walk starts (see [PERFORMANCE_REPORT](PERFORMANCE_REPORT.md)).
- The surgical HUD and the conversation panel were checked on desktop only. Their readability in a headset has not been checked.
- The owner's uncommitted TMP Arabic font changes in the main working folder were **lost** during this integration (see [INTEGRATION_REPORT](INTEGRATION_REPORT.md#incident-uncommitted-font-changes-lost)). The integration branch uses the committed font assets.

## Budget
- The live AI budget is nearly used: **$0.0796 of $2.00 remains**.
- Live verification of the hospital lines and the surgical supervisor needs a new authorisation.
