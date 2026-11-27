# Intelligent Supervisor (v0.2)

Dr. Sara Hassan watches the student and speaks only when it helps learning or safety. Two layers implement this:

| Layer | What it decides | Implementation |
|---|---|---|
| **When** to speak, and **what** the approved message is | Deterministic, rule-based, testable | `Assets/MedSimAI/Scripts/Clinical/SupervisorDirector.cs` + `interventions[]` in the scenario JSON |
| **How** to say it naturally in the moment; answering the student when addressed | Live AI (OpenAI), validated | `Backend/medsim-proxy/lib/live.mjs` (`say` kind `intervention`, `instructorTurn`) |

AI is never called every frame. The director ticks twice per second in plain C#. Each intervention costs about one Luna call plus one speech synthesis, roughly $0.003.

## Events the director observes
Every event comes from the authoritative `ClinicalActionTracker`:

| Event | Source |
|---|---|
| `DOOR_OPENED`, `ENTERED_ROOM` | Physical door, doorway trigger |
| `FACT:<id>` (greeting, identity, consent, history topics) | Student speech to the patient (deterministic keyword match, or AI intent tagged `AI_INTENT`) |
| `INSTRUMENT_PICKED` | Instrument grabbed |
| `INSTRUMENT_ON_PATIENT` | Instrument applied at a valid site |
| `ZONE_REJECTED` | Instrument released or held on the patient at a site that does not accept it (wrong placement) |
| `INSTRUMENT_REMOVED_EARLY` | Cuff or oximeter removed before the reading finished |
| `EXAM:<id>`, `INV:<id>`, `ECG_REVIEWED` | Findings obtained, investigations requested, ECG reviewed |
| `HINT_REQUESTED` | Student asks for help (voice or tablet) |
| `DIAGNOSIS_NAMED`, `DIAGNOSTIC_QUESTION`, `REASONING_GIVEN`, `CONCEPT:<id>`, `SUMMARY_GIVEN` | What the student says to the supervisor |
| `ESCALATION_REQUESTED` | Explicit escalation request (see below) |
| `STATE:<id>` | Authored patient-state change |
| `STUDENT_SPOKE` | Any student utterance (feeds idle detection) |

## Rule model (`interventions[]`, scenario-authored, clinician-reviewable)
| Field | Meaning |
|---|---|
| `trigger.type` | `event` (an event occurs, optionally `count` times within `withinSeconds`), `missing` (`afterSeconds` since session start or `sinceEvent` without the expected event), `state` (a patient-state change), `stage` (entering a clinical stage), `idle` (no clinical activity) |
| `unlessEvents` / `unlessAll` | Suppress if the student already did it (any or all of the listed events) |
| `severity` | `ordinary` (coaching) or `safety` (safety-critical) |
| `priority` | The higher wins when several rules are due |
| `graceSeconds` + `cancelEvents` | Self-correction window: if the student fixes it in time, nothing is said |
| `cooldownSeconds`, `maxRepeats`, `repeatAfterSeconds` | Anti-nagging limits; repeats escalate a **hint ladder** (`lines[0]` → `lines[1]` …) |
| `focus` | Where the doctor looks or points (`patient`, `ecg`, `trolley`, `instrument`, `monitor`) |
| `lines` | **Approved** coaching lines. The AI rephrases them naturally but may not add facts or reveal more of the answer |

**Global policy** (`supervisorPolicy`):
- 20 s minimum between unprompted remarks (safety rules are exempt).
- Never speaks while anyone is speaking or the student is talking: a rule that is due waits until the room is quiet.
- Ordinary rules that are no longer relevant are dropped after 45 s.
- Assessment mode allows only `safety` rules.

## Current rules (ER_CHEST_PAIN_001 v0.2.0 — DRAFT)
| Rule | Severity | Trigger | Self-correction / limits |
|---|---|---|---|
| `I_DETERIORATION` | safety, 100 | Patient enters authored deterioration state | — |
| `I_ESCALATION_DELAY` | safety, 95 | 90 s after ECG review without escalation | 2-step ladder, repeat after 90 s |
| `I_ECG_DELAY` | safety, 90 | No ECG requested by 240 s | 2-step ladder, repeat after 150 s |
| `I_ALLERGY_CHECK` | safety, 70 | Escalation requested before allergies were asked | 4 s grace, cancelled if asked |
| `I_VITALS_BEFORE_REASONING` | ordinary, 60 | Reasoning stage entered without both BP and SpO₂ | — |
| `I_CONSENT_BEFORE_EXAM` | ordinary, 55 | Instrument applied before consent | 6 s grace, cancelled if consent is asked |
| `I_ECG_NOT_REVIEWED` | ordinary, 50 | ECG recorded, not reviewed within 40 s | — |
| `I_WRONG_PLACEMENT` | ordinary, 45 | 2 wrong placements within 40 s | Level 2 gives the instrument-specific guide ("The cuff goes around the upper arm, above the elbow.") |
| `I_EARLY_REMOVAL` | ordinary, 40 | Instrument removed before the reading finished | Cooldown 30 s |
| `I_IDLE` | ordinary, 20 | 90 s without clinical activity | 2 repeats, 120 s apart |

**Correct actions never trigger a rule.** This is verified by `Supervisor_StaysSilent_WhenStudentActsCorrectly`.

## When the student addresses the doctor
The proxy classifies the turn as one of: hint request, investigation request, escalation request, diagnosis statement, diagnostic question, summary/reasoning, question about the case, small talk, or out of scope. It then generates a short Socratic reply in which every number and clinical term must already appear in the findings the student has obtained (grounding check).

The deterministic Unity engine stays authoritative:
- **Escalation counts only when explicit.** An action and a target are both required ("call / page / contact / escalate" + "senior / cardiology / cath lab"), and the statement must not be negated. *"I think it's a STEMI"* is recorded as `DIAGNOSIS_NAMED`, not escalation. *"Is this a STEMI?"* is recorded as `DIAGNOSTIC_QUESTION`.
- **Reasoning credit comes from scenario concepts** (`reasoningConcepts[]`), matched deterministically, never from sentence length. `SUMMARY_GIVEN` requires at least two deterministic concepts. Concepts recognised only by the AI are recorded with the `AI_INTENT` tag and shown as *"AI-interpreted — for instructor review"*.

## Clinical discussion and debrief
- **Discussion.** After escalation (or when the student says they are ready), Dr. Hassan asks the scenario's discussion questions: what they found, what the ECG showed, what they think is happening, and what should happen next. Questions whose concepts were already covered are skipped. Each question allows one follow-up before moving on.
- **Debrief.** A personalised spoken debrief is generated only from the session record: met and missed rubric items, safety-critical misses, interventions, timings, and AI-interpreted items. It is validated (numbers and terms must be in that record) and falls back to a deterministic spoken summary if needed. The detailed written report remains available.

## Tests
`Assets/MedSimAI/Tests/EditMode/LiveAndSupervisorTests.cs` covers:
- silence on correct actions,
- the grace period and self-correction,
- uncorrected hints,
- the wrong-placement ladder,
- safety priority over the cooldown,
- waiting until the room is quiet,
- repeat limits,
- assessment mode,
- both patient-state paths,
- escalation vs. diagnosis separation,
- concept-based reasoning,
- AI-only tagging.

Live behaviour is recorded in [LIVE_AI_TEST_REPORT.md](LIVE_AI_TEST_REPORT.md).

**DRAFT — NOT CLINICALLY VALIDATED.** All rules and lines need clinical review (see CLINICAL_REVIEW.md).
