# MedSim AI — Surgery Module Integration Contract

**Contract version:** 1.0.0  ·  **Baseline commit:** `c1b73d6` (tag `v0.5-stable`, MedSim AI v0.5)  ·  **Integration branch:** `feature/surgery-complete`
**Owner:** Person 4 (AI & Integration Lead). Changes to anything in this file go through a pull request into `feature/surgery-complete` and need the integration lead's approval.

> **DRAFT — NOT CLINICALLY VALIDATED.** The surgical module is an educational simulation for a student project. It is not a substitute for clinical supervision or accredited surgical training, and its scores are not a competency certification. A qualified orthopaedic surgeon must review the clinical content before any real training use.

The code side of this contract lives in `Assets/MedSimAI/Surgery/Shared/`. When this document and the code disagree, **the code is authoritative**: report the mismatch to the integration lead.

---

## 1. Team, branches and ownership

| Person | Role | Branch | Owns (write access by convention) |
|---|---|---|---|
| 1 | Operating-room environment | `feature/surgery-environment` | `Surgery/Environment/`, `Surgery/Scenes/Env_Test.unity` |
| 2 | Anatomy, patient, instruments, implants | `feature/surgery-anatomy-assets` | `Surgery/Anatomy/`, `Surgery/Instruments/`, `Surgery/Scenes/Anatomy_Test.unity` |
| 3 | Surgical mechanics & objective engine | `feature/surgery-mechanics` | `Surgery/Mechanics/`, `Surgery/Scenes/Mechanics_Test.unity`, `Surgery/Tests/Mechanics/` |
| 4 | AI surgeon, assistant, assessment, UI, integration | `feature/surgery-ai-integration` | `Surgery/Shared/`, `Surgery/Characters/`, `Surgery/AI/`, `Surgery/Scenarios/`, `Surgery/UI/`, `Surgery/Integration/`, `Surgery/Scenes/OperatingRoom.unity`, `Surgery/Scenes/SurgeryAI_Test.unity`, `Surgery/Scenes/ScenarioSelect.unity`, `Surgery/Tests/Shared/`, `Surgery/Tests/Integration/`, `Docs/Surgery/` |

(All `Surgery/` paths are under `Assets/MedSimAI/`.)

**Rules**
- Every feature branch starts from `origin/feature/surgery-complete` **after** this contract is merged there:
  `git fetch origin && git switch -c feature/surgery-<yours> origin/feature/surgery-complete`
- Every pull request targets `feature/surgery-complete`. Nothing goes to `master` without the project owner's review (final PR `feature/surgery-complete → master`).
- Don't edit files you don't own. If you need a change in someone else's folder, in `Shared/`, or in the existing MedSim AI code (`Assets/MedSimAI/Scripts`, `Backend/`, `ProjectSettings/`, `Packages/`), open an issue or ask Person 4.
- **Only Person 4 edits `OperatingRoom.unity`.** Everyone else works in their own test scene and delivers **prefabs**.
- Never force-push a shared branch. Never rewrite someone else's commits. Resolve conflicts deliberately.
- Commit `.meta` files with every asset. A missing `.meta` breaks GUID references for everyone.
- Never commit secrets (`*.env`, keys, tokens, `medsim_ai_config.json`). These are already in `.gitignore`.
- Don't commit builds. Binaries go out via an authorised GitHub Release.

## 2. Engine, pipeline and packages (unchanged from baseline, don't upgrade)

| Item | Value |
|---|---|
| Unity editor | **6000.3.29f1** (`ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | **URP 17.3.0** (`com.unity.render-pipelines.universal`) |
| XR | OpenXR 1.19.0, XR Interaction Toolkit 3.7.0, XR Management 4.7.0, VIVE OpenXR (VIVE Focus 3 via VIVE Business Streaming / SteamVR) |
| Input | Input System 1.20.1 (new input system only) |
| Navigation | AI Navigation 2.0.16 (NavMesh for characters) |
| Text | TextMesh Pro (Arabic via the existing `ArabicShaper`) |
| Lip sync | uLipSync 3.1.5 |
| Tests | Unity Test Framework 1.6.0 (NUnit) |

Adding a package needs Person 4's approval. It changes `Packages/manifest.json` for everyone.

## 3. Folder structure and assemblies

```
Assets/MedSimAI/Surgery/
├── Shared/            MedSimAI.Surgery.Shared        (pure C#, noEngineReferences) — ids, events, interfaces, scenario schema, mocks
│   └── Unity/         MedSimAI.Surgery.Shared.Unity  — SurgeryAnchor, *Tag components, SurgeryEventBusHost
├── Environment/       MedSimAI.Surgery.Environment   (P1)
├── Anatomy/           MedSimAI.Surgery.Anatomy       (P2)
├── Instruments/       MedSimAI.Surgery.Instruments   (P2)
├── Mechanics/         MedSimAI.Surgery.Mechanics     (P3)
├── Characters/        MedSimAI.Surgery.Characters    (P4)
├── AI/Core/           MedSimAI.Surgery.AI.Core       (P4, pure C#: supervisor, assessment, action validation, debrief)
├── AI/                MedSimAI.Surgery.AI            (P4, Unity adapters to the existing LiveAIClient / proxy)
├── Scenarios/         authored JSON (Resources/SurgeryScenarios/*.json)
├── UI/                MedSimAI.Surgery.UI            (P4)
├── Integration/       MedSimAI.Surgery.Integration   (P4, the only assembly allowed to reference everything)
├── Scenes/
└── Tests/             Shared/, Integration/ (P4), Mechanics/ (P3), …
```

**Allowed assembly references (no cycles):**

```
Shared  ◄── Shared.Unity ◄── Environment
   ▲             ▲      ◄── Anatomy ◄──┐
   │             │      ◄── Instruments◄┤
   │             │      ◄── Mechanics ──┘ (Mechanics may reference Anatomy/Instruments; never the reverse)
   ├── AI.Core ◄─┼───── AI, Characters, UI   (may also reference the existing `MedSimAI` runtime assembly)
   │             │
   └──────── Integration (references all of the above)
```

- Mechanics **must not** reference AI, Characters, UI, Integration or the network. They publish events and that's all.
- AI/Characters/UI **must not** reference Mechanics, Anatomy, Instruments or Environment. They see the world only through `Shared` interfaces and events.
- Nothing references `Integration`.
- The existing ER code (`MedSimAI` assembly) must not reference any Surgery assembly.

## 4. Units, coordinates and sides

- **1 Unity unit = 1 metre.** Real-world sizes: scalpel ≈ 0.15 m, operating table top ≈ 0.95 m high, adult patient ≈ 1.75 m.
- Unity left-handed, **+Y up**. Prefabs are authored with scale (1,1,1) at the root. Never scale a root to fix import size; fix it in the model importer.
- **Operating room frame** (`ANCHOR_OR_Root` at the floor centre of the room): the operating table's long axis runs along **world Z**, with the **patient's head toward +Z**. The patient lies supine, facing +Y.
- **Patient left/right always means the PATIENT's anatomical side, never the viewer's.** With the frame above, the **patient's RIGHT side is world −X**. The operative site is the **right knee** (`ANCHOR_SurgicalSite_Knee_R`, at negative X relative to the table centreline).
- Naming suffix: `_R` / `_L` = patient's right/left.
- Instrument prefabs: pivot at the grip point, **+Z = toward the working tip**, child `Tip` transform at the tip.

## 5. Integration anchors (`AnchorIds` in code)

Each anchor is an empty GameObject **named exactly as below** with a `SurgeryAnchor` component (`anchorId` = same name). Look it up with `SurgeryAnchor.Find(id)`.

| Anchor | Placed by | Purpose |
|---|---|---|
| `ANCHOR_OR_Root` | P1 | Room origin (floor centre) |
| `ANCHOR_PlayerSpawn` | P1 | Student start pose (inside the door) |
| `ANCHOR_RoomEntry` | P1 | Trigger volume; P1 publishes `player.entered_room` |
| `ANCHOR_OperatingTable` | P1 | Table top centre |
| `ANCHOR_PatientRoot` | P1 | P2's patient prefab is parented here with zero local pose |
| `ANCHOR_SurgicalSite_Knee_R` | P2 | Inside the patient prefab, centre of the right knee |
| `ANCHOR_InstrumentTable` | P1 | Instrument (Mayo) table top centre |
| `ANCHOR_InstrumentSlot_<instrumentId>` | P2 | One per instrument on the tray, e.g. `ANCHOR_InstrumentSlot_inst.scalpel` |
| `ANCHOR_ImplantTable` | P1 | Where implant/trial components sit |
| `ANCHOR_RobotBase` | P1 | Optional robotic arm base |
| `ANCHOR_OverheadMonitor` | P1 | Screen used by the objective panel / overlay |
| `ANCHOR_SurgeonObserve` | P1 | Surgeon's observation spot (head of table, opposite side to the student) |
| `ANCHOR_AssistantHome` | P1 | Assistant's default spot |
| `ANCHOR_AssistantAtInstrumentTable` | P1 | Assistant stands here to pick up instruments |
| `ANCHOR_StudentOperatingPosition` | P1 | Where the student stands to operate (patient's right side) |

Characters walk on a NavMesh that P1 bakes over the floor (keep 0.8 m clear around the table on both sides).

## 6. Prefab contracts

### 6.1 Environment (P1)
`Surgery/Environment/Prefabs/OperatingRoom_Env.prefab`: room, table, lights, instrument and implant tables, monitors, every anchor from §5 that P1 owns, baked NavMesh surface, and the `ANCHOR_RoomEntry` trigger with a component that publishes `player.entered_room` once per session (source `environment`). URP Lit materials only. Target 90 fps on PCVR, so watch real-time lights and shadow casters.

### 6.2 Patient and anatomy (P2)
- `Surgery/Anatomy/Prefabs/SurgicalPatient.prefab`: draped patient, zero pose at `ANCHOR_PatientRoot`, contains `ANCHOR_SurgicalSite_Knee_R`.
- `Surgery/Anatomy/Prefabs/KneeAnatomy_R.prefab`: modular knee, child of the surgical site. For every id in `AnatomyTargetIds`, add a child with a collider (trigger) and `SurgeryAnatomyTag.targetId`. Add a component implementing `IAnatomyStateView` that switches visuals for the states listed in the scenario JSON `targets[].states`.
- Placement targets: a child transform `PlacementTarget_<implantId>` for each implant (the correct authored pose).

### 6.3 Instruments and implants (P2)
- One prefab per instrument: `Surgery/Instruments/Prefabs/<instrumentId>.prefab` (e.g. `inst.scalpel.prefab`). Root carries `SurgeryInstrumentTag` (id, `tip`, `displayNameEn/Ar`), a `Rigidbody`, convex collider(s) and an `XRGrabInteractable`. Pivot and axes as in §4.
- One prefab per implant: `Surgery/Instruments/Prefabs/Implants/<implantId>.prefab` with `SurgeryImplantTag`.
- An instrument tray prefab that puts each instrument at its `ANCHOR_InstrumentSlot_<id>`.
- Desktop mode uses the same prefabs. Grabbing on desktop goes through the existing desktop instrument API, so don't make an instrument VR-only.

### 6.4 Robot (optional; P2 model, P3 behaviour)
`Surgery/Instruments/Prefabs/robot.arm.prefab` with `SurgeryInstrumentTag("robot.arm")`, base at `ANCHOR_RobotBase`. It's used only as a guidance visual. Activating it publishes `tool.activated` with `toolId = robot.arm`. No objective requires it in scenario v0.1.

### 6.5 Mechanics (P3)
`Surgery/Mechanics/Prefabs/SurgeryMechanics.prefab`. One root object that implements `ISurgicalScenarioRuntime` and `IInstrumentRegistry`. It reads the objective definitions (prerequisites, required tools, targets, tolerances) from the authored scenario JSON (§9) and publishes **every gameplay event in §7** through `SurgeryEventBusHost.Publish(...)` with `source = "mechanics"`. Mechanics alone decide whether an interaction is valid. The AI never does.

### 6.6 Characters (P4)
`Surgery/Characters/Prefabs/Surgeon.prefab` and `Assistant.prefab`. Both use the existing humanoid life, speech, lip-sync and NavMesh controllers, adapted to surgical clothing (scrubs, cap, mask). Positions come from the anchors in §5.

## 7. Events

All events are `MedSimAI.Surgery.Shared.SurgeryEvent` and go through the session's `SurgeryEventBus` (scene host `SurgeryEventBusHost`). Unknown ids are rejected by the bus.

### 7.1 Payload

| Field | Type | Meaning |
|---|---|---|
| `id` | string | event id (table below) |
| `source` | string | `mechanics` · `environment` · `integration` · `ai` · `assistant` · `ui` · `mock` |
| `sessionId`, `seq`, `t` | string, long, float | set by the bus (`t` = seconds since `session.started`) |
| `objectiveId` | string | scenario objective id (`obj.*`) the event belongs to (mechanics fills it for every gameplay event) |
| `toolId` | string | `inst.*` / `robot.arm` |
| `targetId` | string | `knee_r.*` |
| `implantId` | string | `implant.*` |
| `state` | string | anatomy state after a change (`intact`, `marked`, `incised`, `exposed`, `guide_placed`, `resected`, `trial_fitted`, `implanted`, `closed`) |
| `reason` | string | rejection reason: `wrong_tool`, `wrong_target`, `prerequisite_missing`, `wrong_side`, `out_of_tolerance`, `not_sterile`, `tool_not_active` |
| `characterId` | string | `surgeon` / `assistant` |
| `success` | bool | false for rejected / failed attempts |
| `positionErrorMm`, `angleErrorDeg` | float | placement accuracy, `-1` = not measured |
| `detail` | string | free text for logs only. Never parsed, never sent to the AI |

### 7.2 Identifiers

| Id | Producer | Required fields | When |
|---|---|---|---|
| `session.started` | Integration | — | Session controller starts a run |
| `session.completed` | Integration | — | All objectives completed (or the student ends the session) |
| `session.reset` | Integration | — | Restart. Mechanics then reset via `ResetForNewSession()` |
| `player.entered_room` | Environment | — | Student enters the OR (once per session) |
| `objective.started` | Mechanics | objectiveId | Objective becomes active (prerequisites met) |
| `objective.completed` | Mechanics | objectiveId | Mechanics confirm completion |
| `tool.grabbed` | Mechanics | toolId, objectiveId | Player picks up an instrument |
| `tool.released` | Mechanics | toolId | Player lets go |
| `tool.activated` | Mechanics | toolId, targetId? | Powered tool on (saw, drill, robot) |
| `anatomy.interacted` | Mechanics | toolId, targetId | Valid contact between a tool and a target |
| `anatomy.state_changed` | Mechanics | targetId, state | Target visual/logic state changes |
| `implant.placement_attempted` | Mechanics | implantId, targetId, success, errors | Each placement attempt |
| `implant.placement_completed` | Mechanics | implantId, targetId, errors | Component seated within tolerance |
| `interaction.rejected` | Mechanics | reason, toolId?, targetId? | Invalid attempt (nothing changes in the world) |
| `help.requested` | AI | — | Student asked for help (voice/UI) |
| `hint.given` | AI | objectiveId | Supervisor gave a hint |
| `supervisor.intervened` | AI | objectiveId?, reason | Supervisor intervention / safety stop |
| `assistant.action_requested` / `_completed` / `_failed` | Assistant | characterId, detail | Embodied action lifecycle |
| `assistant.handoff_offered` | Assistant | toolId | Assistant holds an instrument out |
| `assistant.handoff_completed` | Assistant | toolId | **Only** after mechanics reported `tool.grabbed` for that tool |

**Assessment credit rule:** only events with `source = "mechanics"` from the gameplay group (`objective.*`, `tool.*`, `anatomy.*`, `implant.*`, `interaction.rejected`) can earn credit. AI, assistant, UI and mock events never mark objectives or award points. Saying "I made the incision" earns nothing.

### 7.3 Ordering guarantees from mechanics
1. `objective.started(X)` comes before any gameplay event tagged `X`.
2. `objective.completed(X)` is published at most once per session and comes after the events that justified it.
3. A rejected attempt publishes `interaction.rejected` and **does not** change anatomy state.
4. On `session.reset`, mechanics restore every target to `intact` and every tool to its slot before the next `objective.started`.

## 8. Runtime interfaces (`Shared/Runtime/SurgeryInterfaces.cs`)

| Interface | Implemented by | Consumed by |
|---|---|---|
| `ISurgicalScenarioRuntime` | P3 | P4 (supervisor, UI objective panel, assistant permissions) |
| `IInstrumentRegistry` | P3 (using P2 tags) | P4 assistant hand-over & "where is the X" |
| `IAnatomyStateView` | P2 | P3 |
| `ISurgeryCharacterActuator` | P4 | P4 AI (validated actions only) |
| `ISurgeryEventSink` / `ISurgeryEventSource` (`SurgeryEventBus`) | Shared | everyone |

Mocks for each (`Shared/Mocks/SurgeryMocks.cs`): `MockScenarioRuntime`, `MockInstrumentRegistry`, `MockAnatomyStateView`, `MockCharacterActuator`, `ManualClock`, `MockEventScripts`. Use them in your test scene so you never wait for another branch.

## 9. Scenario and session identifiers

| Item | Value |
|---|---|
| Surgical scenario id | `OR_TKA_TRAINING_001` (file `Surgery/Scenarios/Resources/SurgeryScenarios/OR_TKA_TRAINING_001.json`) |
| Schema | `SurgicalScenarioData` (`Shared/Runtime/SurgicalScenarioData.cs`), validated by `SurgicalScenarioValidator` |
| Clinical scenario id | `ER_CHEST_PAIN_001` (existing, untouched) |
| Session id | GUID string created by the surgical session controller per run. Each run starts a fresh `SurgeryEventBus.BeginSession` |

**Objective ids (v0.1, agreed with P3):** `obj.site_confirmation` → `obj.incision` → `obj.exposure` → `obj.femoral_resection` → `obj.tibial_resection` → `obj.trial_reduction` → `obj.implant_femoral` and `obj.implant_tibial` → `obj.closure`. The prerequisites, tools, targets and tolerances for each one are in the JSON.

**Isolation:** the surgical module never loads `ER_CHEST_PAIN_001`, never uses the ER `ClinicalSession`, ER personas (`Patient`, `Instructor`) or the ER rubric, and starts its own proxy session with its own scenario id. Returning to the menu ends the session on both sides.

## 10. Clinical validation status

- `review.status = "DRAFT"` until a named, qualified orthopaedic surgeon approves the content. The validator rejects `APPROVED` without a reviewer.
- UI shows **"DRAFT — NOT CLINICALLY VALIDATED" / «مسودة — غير معتمدة سريرياً»** on the scenario card, in the OR and in the debrief.
- Nobody generates "validated" procedure content from an AI model. The AI may only rephrase approved scenario text, and the existing grounding rules apply.

## 11. Testing requirements

- **Every assembly has EditMode tests** in `Surgery/Tests/<Area>/` with its own test asmdef (`UNITY_INCLUDE_TESTS`).
- Pure logic stays engine-free where possible so it also runs **without Unity**:
  `cd Tools/OffUnityTests/Surgery && dotnet run` (needs only the .NET 8 SDK, no NuGet).
- Mechanics (P3): a test that replays `MockEventScripts`-style sequences and checks the event ordering guarantees in §7.3.
- Prefabs (P1/P2): a test or editor check that every required anchor/tag exists with the right id (no missing scripts).
- Before opening a PR: the project compiles with **zero errors**, your tests pass, and the existing ER tests still pass (Test Runner → EditMode + PlayMode). Put the results in the PR description.
- Never use paid API calls in automated tests. Use mocks. Live AI tests need explicit budget authorisation from the project owner.

## 12. Merge order and PR checklist

Integration order: **1** Shared contract → **2** Environment → **3** Anatomy & instruments → **4** Mechanics → **5** AI, characters & final integration.

Every PR into `feature/surgery-complete` must state:
- [ ] Files only in your owned folders (or the agreed exceptions)
- [ ] Asset sources and **licences** listed (add them to `ASSET_CREDITS.md` in your PR description; P4 merges the credits)
- [ ] Required prefabs / anchors / ids from this contract present
- [ ] No missing scripts, all `.meta` files committed
- [ ] No secrets, no builds, no files > 100 MB
- [ ] Test results (Unity Test Runner screenshot or log)
- [ ] Known limitations

## 13. Contract change log

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2026-11-27 | Initial contract on baseline `c1b73d6` |
