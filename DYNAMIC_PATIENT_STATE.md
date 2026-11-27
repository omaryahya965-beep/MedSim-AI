# Dynamic Patient State (v0.2 — DRAFT, NOT CLINICALLY VALIDATED)

Mr. Reyes's condition can change during the encounter, but **only through authored, versioned, deterministic transitions** in `Resources/Scenarios/ER_CHEST_PAIN_001.json` (`patientStates[]`, `initialStateId`, `stateTransitions[]`):

- The AI receives the current state read-only and cannot change it, invent a condition, or change vital signs.
- Correct actions do **not** improve the patient unless a transition explicitly says so. None does in v0.2.0.

The states below are **proposed by the software team for demonstration**. They are not approved medical findings and must be reviewed by a qualified clinician.

## States
| Id | Label | HR | BP | SpO₂ | RR | Pain | Expression / breathing | Approved statements (patient may voice) |
|---|---|---|---|---|---|---|---|---|
| `S_PRESENTATION` | Initial presentation | 98 | 152/94 | 96 | 20 | 8/10 | discomfort, slightly fast | "The pain is still there, heavy in the middle of my chest." "I feel sweaty and a bit sick." |
| `S_ONGOING` | Ongoing symptoms (no early ECG) | 104 | 148/92 | 96 | 22 | 8/10 | distress, fast | "It is not getting any better, doctor." "I am sweating more now." |
| `S_DETERIORATION` | Scenario-defined deterioration (delayed care) | 112 | 132/84 | 94 | 24 | 9/10 | severe distress, fast | "It is getting worse. I feel really unwell." "I feel faint and sick." |
| `S_HANDOVER` | Escalated — senior team on the way | inherited | inherited | inherited | inherited | unchanged | anxious but reassured | "Thank you for getting help, doctor." |

## Transitions
| Id | From | To | Trigger |
|---|---|---|---|
| `T_ONGOING` | PRESENTATION | ONGOING | 300 s elapsed, **unless** ECG requested or escalated |
| `T_DETERIORATE` | PRESENTATION, ONGOING | DETERIORATION | 540 s elapsed, **unless** escalated |
| `T_HANDOVER` | any of the above | HANDOVER | Event `ESCALATION_REQUESTED` |

**Example paths:**
- **Path A (timely care):** PRESENTATION → HANDOVER. Vitals are unchanged.
- **Path B (no early ECG, no escalation):** PRESENTATION → ONGOING (5 min) → DETERIORATION (9 min) → HANDOVER once escalated. Vitals stay at the deteriorated values.
- **Path C (early ECG but no escalation):** PRESENTATION → DETERIORATION (9 min) → HANDOVER.

## What changes when the state changes
- **Monitor:** HR, RR and the pleth/resp traces follow the state immediately. NIBP and SpO₂ show the new values when re-measured (reassessment).
- **Exam results:** BP and SpO₂ measurements read the current state.
- **Patient character:** facial discomfort intensity and breathing rate follow the state (`PatientInteractionController.ApplyCondition`). The Animator `Condition` parameter is ready for posture clips. If the room is quiet, he voices the first approved statement, phrased naturally by Live AI and grounded in that statement.
- **Supervisor:** the `I_DETERIORATION` safety rule fires on deterioration. After `S_HANDOVER`, the doctor moves on to the clinical discussion.
- **AI context:** both personas receive the current state's label and context. The patient may describe only that state's approved statements and his approved history facts.
- **Event log:** a `STATE:<id>` event is recorded, so the debrief and assessment can see which path the session took.

## Engineering
- `PatientState` in `Scripts/Clinical/ClinicalSession.cs` exposes `EvaluateConditionTransitions`, `ConditionChanged` and `ConditionPath`.
- `ClinicalSession.Tick()` runs about twice per second from `ClinicalScenarioManager`.
- `ClinicalScenarioManager.scenarioTimeScale` and `AdvanceScenarioTime()` exist for demonstrations and tests only.
- Validator rules check for unknown states, implausible vitals, invalid triggers and a missing initial state.
- Tests: `DynamicState_PathA_TimelyCare_…` and `DynamicState_PathB_DelayedCare_…` (EditMode). The live grounded responses for each state are in LIVE_AI_TEST_REPORT.md (Test F).
