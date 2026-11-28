# Pull request review

Reviewed on 2026-11-28 against `feature/surgery-complete` @ `e4fbd9c`. Each PR was merged into a local candidate and compiled and tested with **Unity 6000.3.29f1**. None of the open PRs has an approving review. Nothing was merged on GitHub.

## Status at the start of the integration
| PR | Branch @ head | State | Review | Mergeable |
|---|---|---|---|---|
| #1 | `feature/surgery-environment` @ `5c2ba63` | **Merged** (as `e4fbd9c`) | — | — |
| #3 | `feature/surgery-environment` @ `c40f7da` | Open, **draft** | none | yes |
| #5 | `feature/surgery-anatomy-assets` @ `87c6c85` | Open | none | yes |
| #4 | `feature/surgery-mechanics` @ `c85a89c` | Open | none | yes |
| #2 | `feature/surgery-ai-integration` @ `68297ad` | Open | none | yes |

## PR #1: operating room prototype (merged earlier)
- **Present in the integration branch:** `OperatingRoom.prefab`, the robot prefab, materials and the NavMesh asset. PR #3 superseded parts of it.
- Nothing to merge again.

## PR #3: operating room contract alignment (draft)
- **Content:**
  - 13 `ANCHOR_*` anchors as `OperatingRoomAnchor : SurgeryAnchor`, a sound workaround for the serialization problem below;
  - `RoomEntryPublisher`, `OperatingRoom_Env.prefab`, `Env_Test.unity`;
  - updated equipment, and the instrument anchor moved to the Mayo stand.
- **Unity 6000.3.29f1:** compiles; 7 of 7 environment tests pass. The contributor's report says the same.
- **Findings, fixed in the integration** (`c483ded`, edited in Person 1's own generator and regenerated with it):
  - Surgical beams, student position and assistant home assumed a straight leg, 0.39 m from the real (flexed) knee site.
  - The robot base was 2.2 m from the knee, beyond the arm's reach.
  - The back table held placeholder instrument trays.
- **Remaining:**
  - The vestibule's outer wall had to be hidden in the hospital (the room was a closed box).
  - The robot carries no `robot.arm` tag; the integration binds it by joint names.
  - Performance was measured on an Apple M3. RTX 3050 figures are in [PERFORMANCE_REPORT.md](PERFORMANCE_REPORT.md).
- **Recommendation:** take PR #3 out of draft once Person 1 accepts the generator changes (or applies them in their branch), then merge.

## PR #5: surgical patient, knee anatomy, instruments, implants (Person 2)
- **Reported results:** EditMode 133/133, PlayMode 33/33, proxy 34/34, validator 0 issues.
- **Verified on the combined candidate:**
  - its 14 EditMode and 4 PlayMode tests pass;
  - validator 0 issues;
  - assets load in the hospital and work with the real mechanics.
- **Findings, fixed in the integration:**
  - The prefabs relied on runtime tag bridges because of the shared-file bug. They now serialize native tags (`b1a2f24`).
  - The toes poked through the drape (originally misidentified as fingertips) and the gown showed through over the chest (`4e8857b`).
- **Remaining:**
  - The anatomy is a healthy atlas knee.
  - The flexion pose is fixed, and there are no knee LODs.
  - All geometry values are illustrative (see CLINICAL_REVIEW).
- **Recommendation:** merge after review. The integration changes to the builder and prefabs are in the candidate.

## PR #4: surgical mechanics (Person 3)
- **Reported:** "all tests passed, 0 compile errors" on Unity **6000.0.85f2**. That was a compile check; the tests had not been run.
- **Verified on 6000.3.29f1:**
  - It **compiles**, with one warning (an unused field).
  - **5 of 12 tests failed.** All 12 now pass after these fixes:
    - lazy dependency wiring (EditMode `AddComponent` does not call `Awake`);
    - the `??` fake-null bug in `SurgeryMechanicsRoot`;
    - the shared event-bus serialization.
- **Functional findings, fixed in the integration** (`8c34501`):
  - First-contact completion of multi-step objectives.
  - Implant reset to a single point.
  - Instrument reset not restoring the parent or physics state.
  - The robot controller moving the GameObject it is attached to.
  - No real left/right detection.
  - Desktop `R` resetting only the mechanics; it now restarts the whole session.
- **Not changed (documented):**
  - Contact is "any collider of the tool enters the target trigger", not the tool tip.
  - Tissue changes are discrete state swaps.
  - In VR, contact triggers can register accidental touches as rejected attempts.
- **Recommendation:** merge after review together with the integration fixes. Person 3 should review the sequencing rules in `SurgeryInteractionValidator`.

## PR #2: AI surgeon, assistant, assessment, HUD, proxy routes (Person 4)
- **Reported:** off-Unity tests passing; Unity scenes not verified.
- **Verified on 6000.3.29f1:**
  - compiles;
  - the Unity EditMode tests (`SurgeryAITests`, contract, scenario) pass;
  - proxy 46/46 (including `surgery.test.mjs`).
- **Off-Unity harness:** not run here. It needs the .NET 8 targeting pack, which cannot be restored offline on this machine.
- **Findings, fixed in the integration:**
  - **`Restart()` lost objective 1's `objective.started`.**
  - The runtime was chosen by scene order.
  - **A second doctor persona ("Dr. Lina Haddad")**, replaced by the persistent Dr. Sara Hassan.
  - HUD overlap and unshaped Arabic; missing glyphs; the HUD staying visible outside the room.
  - "Return to menu" loaded a separate scene (now: back to the corridor).
  - The attire code: whole-word matching, placement on the rig, plain fabric.
  - The assistant reused the patient's body.
  - The surgical-independence test forbade the shared doctor's name; that one assertion was updated with a reason. Clinical-case leakage is still forbidden.
- **Mock mechanics:** `MockMechanicsDriver` remains for Person 4's own test scene. In the hospital `allowMockMechanics = false`, and a test asserts there are no `mock` events.
- **Recommendation:** merge after review. The CI workflow (`.github/workflows/unity-ci.yml`) was not exercised here.
