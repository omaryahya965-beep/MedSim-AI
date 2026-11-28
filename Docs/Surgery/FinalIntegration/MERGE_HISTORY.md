# Merge history

## Branches
- **Base:** `feature/surgery-complete` @ `e4fbd9c`. It already contained contract 1.1, v0.6 (`v0.6-stable` = `4108aaf` on `master`) and PR #1.
- **Integration branch:** `integration/surgery-final`, created from the base. All work was done in a separate worktree (`D:/Medical-Test/MedSim-Integration`), so the main working folder and its uncommitted user files were never touched by Git.
- **Not used:** force-push, reset, rebase, history rewriting, or direct commits to `master` or `feature/surgery-complete`. The tag `v0.6-stable` is unchanged.

## Order (dependency order from the brief: environment → anatomy → mechanics → AI)
| # | Commit | What | Conflicts |
|---|---|---|---|
| 1 | `7824e0c` | Merge PR #3 `feature/surgery-environment` @ `c40f7da` (Person 1, draft) | none |
| 2 | `b800482` | Merge PR #5 `feature/surgery-anatomy-assets` @ `87c6c85` (Person 2) | none |
| 3 | `403408a` | Merge PR #4 `feature/surgery-mechanics` @ `c85a89c` (Person 3) | none |
| 4 | `dfc2664` | Merge PR #2 `feature/surgery-ai-integration` @ `68297ad` (Person 4) | none |
| 4b | `c8602a2` | Merge PR #2 follow-up @ `b577665` (Person 4 pushed it after step 4: CI results in docs, CI `paths-ignore`; no code) | none |

Each merge was compiled before the next one. The full test suite was run after the fixes below.

## Integration commits
| Commit | Subject |
|---|---|
| `751cb58` | fix(surgery): resolve shared prefab serialization (one MonoBehaviour per file; GUIDs kept; serialized hosts migrated) |
| `8c34501` | fix(surgery): run the mechanics on Unity 6000.3 and follow the authored objective steps |
| `b1a2f24` | feat(surgery): serialize the contract tags natively in the anatomy and instrument prefabs |
| `c483ded` | fix(surgery): align the operating room with the flexed-knee surgical site |
| `74a1c6a` | feat(integration): host hooks so the clinical and surgical layers can live in one hospital |
| `3389029` | feat(hospital): integrate the two medical training rooms into one hospital |
| `d5019a5` | feat(ai): one persistent doctor guide across the hospital, with tests |
| `4e8857b` | fix(surgery): drapes cover the left toes and the gown |
| `a7ee5b4` | feat(hospital): unified conversation UI, staff attire, exe self-test and integrated builds |
| `9ed089c` | fix(hospital): surgical HUD hidden outside Room 02; evidence from the integrated Desktop exe |
| `1cef175` | feat(hospital): distinct scrub assistant (Rocketbox `Medical_Male_04`, MIT) |
| `068988c` | docs(integration): final integration documentation, licence texts and README |

## Changes inside contributors' areas
These are listed so each owner can review them:
- **Person 1 (`Surgery/Environment`):**
  - `OperatingRoomBuilder` constants: knee site, student and assistant anchors, robot dock, light aim.
  - `RoomEquipment` placeholder trays removed; `ImplantTable` anchor moved.
  - The room was regenerated with Person 1's own menu command.
- **Person 2 (`Surgery/Anatomy`):**
  - `SurgeryAssetBuilder`: native tags, left-knee marking trigger, fitted drape.
  - Tag bridges removed; validator and tests updated.
- **Person 3 (`Surgery/Mechanics`):**
  - Lazy wiring.
  - Sequencing in `SurgeryInteractionValidator`; `ObjectiveProgress` in the engine.
  - Implant and instrument reset.
  - Robot binding.
  - Desktop adapter: unparenting and the `ResetRequested` hook.
  - New `SurgerySequencingTests`.
- **Person 4 (`Surgery/Integration`, `AI`, `UI`, `Characters`, proxy):**
  - `Restart` order and runtime choice.
  - `hostManaged` hooks; HUD flags and glyphs.
  - Attire matching.
  - Persona renamed to Dr. Sara Hassan; one test assertion updated (with reason).
- **Shared (`Shared/Unity`):** `SurgerySceneTags.cs` split into one file per class.
- **v0.6 clinical code:**
  - Small additive hooks only (`UtteranceInterceptor`, `ExtensionHandler`, `GoToPoint`, `hostManaged`, `displayNameAr`, `SurgeryKeysActive`).
  - With no hospital present, behaviour is unchanged: all 29 clinical Play-mode tests pass.

## GitHub delivery
| Item | State |
|---|---|
| `integration/surgery-final` | Pushed to `origin` (normal push, no force) |
| Final PR `integration/surgery-final` → `master` | Opened for owner review. **Not merged.** |
| PR #2, #4, #5 → `feature/surgery-complete` | **Not merged.** None has an approving review. |
| PR #3 → `feature/surgery-complete` | **Not merged.** Still a **draft**, with no review. |
| Contributor branches | Not modified, not deleted |

**Blocker for merging the contributor PRs remotely:** there is no approval, and PR #3 is a draft. Their content reaches the final PR only through the local merge commits above. This is a local integration, not a remote merge of each PR. The owner can either:
1. approve and merge #3 → #5 → #4 → #2 into `feature/surgery-complete` (GitHub will then show the integration branch as containing them), or
2. review the final PR as one unit.
