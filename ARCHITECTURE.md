# Architecture (v0.2)

```
 Student (VIVE Focus 3: voice push-to-talk, hands/controllers, physical door & instruments)
   │
   ├─ VoiceInteractionManager ── mic (in memory) ─► LiveAIClient.Transcribe ─► proxy /v1/stt ─► gpt-transcribe
   │                                                                   │
   │                                ConversationOrchestrator ◄─────────┘ (text)
   │   1. Deterministic tracking (authoritative): ProcessStudentQuestion / ProcessInstructorStatement
   │        → FACT/EXAM/INV events, explicit escalation, DIAGNOSIS_NAMED vs DIAGNOSTIC_QUESTION, CONCEPT:<id>
   │   2. InputGuard (manipulation, real-patient advice, dosing)
   │   3. LiveProvider → proxy /v1/turn  (ids only: scenario, persona, stage, state id, event ids)
   │        proxy: authoritative scenario → persona prompt → gpt-6-luna strict JSON
   │               → deterministic grounding check → approved fallback if needed → runtime speech job
   │   4. ApplyLive: AI-only recognitions recorded with the AI_INTENT tag (instructor review)
   │   5. Text-only fallback (OfflineScriptedProvider) when the AI service is unavailable — clearly labelled
   │                                                                   ▼
   │   LiveAIClient.FetchAudio(/v1/audio/:id) ◄─ gpt-realtime-2.1-mini (voice actor, transcript fidelity check)
   │        → CharacterSpeechController (spatial audio, visemes driven by real audio amplitude, subtitles)
   │
   ├─ Instruments (XRGrabInteractable) → PlacementZones → ClinicalSession.PerformExam
   │        + observation events: INSTRUMENT_PICKED / ON_PATIENT / ZONE_REJECTED / REMOVED_EARLY
   ├─ HospitalDoor (lever → latch → hinge) → DOOR_OPENED;  RoomEntryTrigger → ENTERED_ROOM
   │
   └─ ClinicalScenarioManager (ticks 2×/s)
          ├─ ClinicalSession (pure C#, authoritative)
          │    ├─ ScenarioData v0.2.0 (JSON, validator, review gate — DRAFT)
          │    ├─ ClinicalStateMachine (stages) · PatientState (authored condition state machine)
          │    ├─ ClinicalActionTracker (event log) · StatementAnalysis (concepts, escalation, diagnosis)
          │    └─ AssessmentEngine (rubric) → SessionStore → FeedbackComposer
          └─ SupervisorDirector (rules: grace, priority, cooldown, repeats, ladder, quiet gate)
                 → SessionManager.OnIntervention → doctor gesture/gaze + LiveAIClient.Say(kind=intervention)

 SessionManager journey: corridor welcome (live) → door → doctor walks in (DoctorMovement/NavMesh)
   → assessment (silent observation, interventions) → discussion (scenario questions) → spoken debrief → report
```

## Key rules (enforced in code)
- **Authoritative scenario.** Facts, vitals, findings, results, transitions, rubric and expected reasoning come only from the scenario JSON. The AI never writes to it.
- **Ids only from the client.** Unity sends ids, never clinical text. The proxy loads the scenario itself and ignores client-supplied facts.
- **Grounded speech.** Generated text is accepted only if every number and clinical term is present in the approved corpus for that turn (`lib/grounding.mjs`). Otherwise approved scenario text is spoken.
- **Deterministic grading.** Grading uses deterministic matching plus physical events. AI-only interpretations are tagged and flagged for review, never silently scored.
- **Runtime speech only.** No pre-recorded character dialogue exists in the project (`NoPrerecordedCharacterDialogue_InProject` test).
- **Secrets stay server-side.** Provider keys live only on the proxy machine (environment variable, or a secrets file outside the repo). The in-app budget cap, rate limits and timeouts are enforced by the proxy.

## Folders
| Path | Contents |
|---|---|
| `Assets/MedSimAI/Scripts/Clinical` | `ScenarioData` (v0.2 schema), `ClinicalSession` (+ `PatientState` condition machine), `StatementAnalysis`, `SupervisorDirector` |
| `Assets/MedSimAI/Scripts/AI` | `LiveAIClient`, `LiveProvider`, `ConversationOrchestrator`, `AIProviderManager`, safety (`InputGuard`, `ResponseValidator`), offline text provider |
| `Assets/MedSimAI/Scripts/Voice` | `VoiceInteractionManager` (push-to-talk, live STT, runtime speech, `SayLive`) |
| `Assets/MedSimAI/Scripts/Characters` | `DoctorMovement`, `HumanoidLifeController`, `FaceRig`, `CharacterSpeechController`, instructor/patient controllers |
| `Assets/MedSimAI/Scripts/XR` | `XRPlayerController` (+0.40 m lift), `HospitalDoor`, `RoomEntryTrigger` |
| `Assets/MedSimAI/Scripts/Instruments` | Instruments, zones, monitor, ECG |
| `Assets/MedSimAI/Editor` | Scene builders (`MedSimSceneBuilder`, `MedSimRedesignBuilder`), lighting, character setup |
| `Backend/medsim-proxy` | `server.mjs`, plus `lib/` (secrets, ledger, openai, grounding, live), `tools/` (live harness, Unity config writer), tests |

See also [INTELLIGENT_SUPERVISOR.md](INTELLIGENT_SUPERVISOR.md), [DYNAMIC_PATIENT_STATE.md](DYNAMIC_PATIENT_STATE.md), [AI_SETUP.md](AI_SETUP.md) and [SECURITY_AND_PRIVACY.md](SECURITY_AND_PRIVACY.md).
