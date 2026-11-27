# Architecture

```
Student (voice / tablet / instruments)
   │
   ├─ VoiceInteractionManager ── push-to-talk → STT (proxy | Windows dictation) ─┐
   │                                                                          ▼
   │                      ConversationOrchestrator
   │   1. ClinicalSession.ProcessStudentQuestion / ProcessInstructorStatement  ← deterministic event tracking (grading)
   │   2. InputGuard (manipulation / real-patient / dosing)                    ← code-enforced refusals
   │   3. Investigation results spoken verbatim from scenario data
   │   4. ClinicalContextProvider → persona-filtered READ-ONLY context
   │   5. IAIProvider (BackendProxyProvider | OfflineScriptedProvider), timeout → offline fallback
   │   6. ResponseValidator (fact ids, numbers, diagnosis, dosing, state-change, references)
   │                                                                          ▼
   │   TTS (pre-rendered approved audio | proxy TTS) → CharacterSpeechController (spatial audio, jaw, subtitles)
   │
   ├─ Instruments (XRGrabInteractable) → PlacementZone detection → ClinicalSession.PerformExam / RequestInvestigation
   │
   └─ ClinicalScenarioManager ─ ClinicalSession (authoritative, pure C#)
          ├─ ScenarioData (versioned JSON, ScenarioValidator, review gate)
          ├─ ClinicalStateMachine (8 stages; assessment stages in any order)
          ├─ PatientState (vitals change only through authored transitions; defensive copies)
          └─ ClinicalActionTracker → AssessmentEngine (rubric) → SessionStore (JSON) → FeedbackComposer
```

## Key rules (enforced in code)
- The LLM never receives the rubric, the diagnosis (until feedback), or unrevealed results. It cannot call state-changing APIs. Its output is text only and is validated before use.
- Grading uses deterministic keyword matching of the student's own words plus instrument events. It never uses LLM output.
- Monitor and instrument readings come only from `PatientState`.

## Folders (`Assets/MedSimAI`)
| Folder | Contents |
|---|---|
| `Scripts/Clinical` | `ScenarioData`, `ScenarioValidator`, `ClinicalSession`, state machine, `PatientState`, `ClinicalActionTracker`, `HistoryMatcher` |
| `Scripts/AI` | Contracts/interfaces, `ClinicalContextProvider`, `InputGuard`, `ResponseValidator`, providers, `ConversationOrchestrator`, `AIProviderManager`, `ScriptedLines` |
| `Scripts/Voice` | `VoiceInteractionManager` |
| `Scripts/Characters` | `CharacterSpeechController`, `HumanoidLifeController` (gaze, breathing, blink, gestures), instructor and patient controllers |
| `Scripts/Instruments` | `MedicalInstrumentBase`, `PlacementZone`, stethoscope, BP cuff, oximeter, ECG cart, sanitiser, `PatientMonitorController` + `ECGTracingRenderer` |
| `Scripts/Assessment` | `AssessmentEngine`, `SessionStore`, `FeedbackComposer` |
| `Scripts/UI` | `UIFactory` (brand), `MedSimUI` (menu board, tablet, subtitles) |
| `Scripts/XR` | `XRPlayerController` (floor tracking, height, seated, recentre, smooth-move toggle, pause) |
| `Scripts/Core` | `ClinicalScenarioManager`, `SessionManager`, `PerformanceManager`, `MeshKit`, procedural audio |
| `Editor` | `MedSimSceneBuilder` (scene generator), `MedSimProjectSetup` |
| `Resources` | Scenario JSON, pre-rendered voice WAVs |
| `Tests` | EditMode (36) and PlayMode (3) |

Interfaces for provider swapping: `IAIProvider`, `ISpeechToTextProvider`, `ITextToSpeechProvider`, `IClinicalContextProvider`, `IConversationOrchestrator`, `IResponseValidator`.

## Phase 2 additions
- `FaceRig`: composes ARKit face units and Microsoft visemes across the body, eyebrow, eyelash and hair meshes.
- `HumanoidLifeController`: rig-agnostic breathing, gaze, blink, expressions and gestures.
- `CharacterSpeechController`: viseme lip sync.
- `MedSimCharacterSetup` / `MedSimRealCharacters` (Editor): import, materials, posing, and zone fitting by raycasting the baked skin.
- `GroundedProxyProvider` and `ConversationOrchestrator.ApplyGrounded`: classification-only AI. Clinical text is rendered locally from approved content, and AI-only recognitions are tagged `AI_INTENT`.
- `LanguagePack` (separate `*.ar.json`, exact approved-string mapping) and `ArabicShaper` (Presentation Forms-B plus RTL).
- `Backend/medsim-proxy/server.mjs`: server-side scenario load and validation, JSON-schema enums, free-text validators, no key logging.
