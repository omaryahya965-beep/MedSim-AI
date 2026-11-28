# Test report: final integration

**Date:** 2026-11-28. **Unity:** 6000.3.29f1, on the combined project (`integration/surgery-final` @ `1cef175`). **Machine:** Windows 11, RTX 3050 Laptop GPU.

The totals below come from running the combined project. They are **not** the sum of the contributors' reported totals. All runs were offline (`AIProviderManager.ForceOffline`, proxy fake provider): **no paid AI calls**.

## Totals (actual runs)
| Suite | Result |
|---|---|
| Unity **EditMode** (all assemblies) | **231 / 231 passed** |
| Unity **PlayMode** | **42 / 42 passed**: clinical v0.6 29, anatomy 4, hospital 9 |
| Proxy (`node --test`: `server.test.mjs`, `surgery.test.mjs`) | **46 / 46 passed** |
| Person 2 asset validator (*MedSim AI → Surgery → Validate anatomy & instrument assets*) | **0 issues** |
| Desktop exe journey (`-hospitalSelfTest`) | **all steps passed, 0 exceptions** |
| PCVR exe with `-desktop` (no headset) | journey passed; OpenXR loader present; `xrCreateInstance` fails without a runtime (expected) |
| Off-Unity C# harness (PR #2) | **Not run.** It needs the .NET 8 targeting pack, which is not available offline here. |
| GitHub Actions CI (PR #2 workflow) | Not re-run on the integrated candidate. Person 4 reports one run on their branch (EditMode 167/167, PlayMode 29/29, Windows build), with mock mechanics. |

No test was deleted, skipped or weakened to obtain these results. One assertion in `SurgicalScenarioTests.Tka_IsIndependentFromChestPain` now expects the shared doctor's name ("sara hassan"), because there is deliberately one doctor. The test still forbids clinical-case leakage (Reyes, chest pain, ECG, …) into the surgical scenario.

## Failures found during integration (before fixes)
| Where | Failure | Fixed by |
|---|---|---|
| Person 3 mechanics tests | 5 of 12 failed on 6000.3.29f1 (null dependencies in EditMode; fake-null `??`; no event bus because of the shared-file serialization) | `751cb58`, `8c34501` |
| Combined run | Objectives completed on first contact; objective 1's start lost on restart; HUD overlap; surgical HUD persisting in the corridor | `8c34501`, `74a1c6a`, `a7ee5b4`, `9ed089c` |

## Stage A: individual contributions
| Contribution | Run on the combined project | Result |
|---|---|---|
| Person 1 environment | `OperatingRoomTests` (anchors, contract, NavMesh, equipment), `Env_Test.unity` | 7 of 7 passed |
| Person 2 anatomy and instruments | `SurgeryAnatomyAssetTests` (14), `SurgeryAnatomyRuntimePlayTests` (4), validator | passed, 0 issues |
| Person 3 mechanics | `SurgeryMechanicsTests` (12, original, unchanged) + `SurgerySequencingTests` (4, new) | 16 of 16 after fixes |
| Person 4 AI | `SurgicalSupervisorTests`, `SurgicalAssessmentTests`, `SurgeryActionTests`, `SurgerySessionCoreTests` (in `SurgeryAITests.cs`), `SurgeryContractTests`, `SurgicalScenarioTests` | passed |

## Stage B: combined compilation
- 0 compile errors.
- Warnings: one unused field in Person 3's code, plus pre-existing v0.6 obsolete-API warnings.
- No missing scripts in the hospital scene or the surgery prefabs: checked by the validator and the `ContractTags_AreSerializedInPrefabAssets_ExactlyOnce` test.

## Stage C: surgical integration in the production room
`Guide_WalksStudentToOperatingRoom_…` and `Surgery_FullScenario_…` assert:
- `UsingMockMechanics == false`;
- the runtime is `SurgeryMechanicsRoot`;
- every `objective.completed` comes from source `mechanics`;
- there are **no `mock` events** on the bus.

The exe log shows the same: `mock=False, runtime=SurgeryMechanicsRoot`.

## Stage D: hospital navigation (PlayMode + exe)
| Check | Test / evidence |
|---|---|
| Spawn in reception, not beside a patient; exactly one doctor | `Start_InReception_DoctorWelcomes_NoSessionRunning`; exe `01` |
| Doctor welcome | same test; exe `02` |
| Room selection (voice, Arabic) and real walk (max step < 0.25 m, no teleport) | `Guide_WalksStudentToOperatingRoom_NoTeleport_…`; exe `04`, GIF |
| Enter operating room → session | same test; exe log `Corridor → Room02` |
| Return to corridor → session ends; Room 01 starts clean | `LeavingTheOperatingRoom_EndsTheSession_ThenRoom01StartsClean`; exe log |
| Wait / walk with me / restart / F10 | `Corridor_Wait_WalkWithMe_Restart_AndMenuShortcut` |
| Session isolation | Room 01 tracker has no `inst.`/`knee_r`/`implant` ids (test + exe: `surgical ids present: False`) |
| Intent parsing (Arabic, English, mixed, ambiguous, inside rooms) | `HospitalIntentParserTests`, 34 EditMode cases |

## Stage E: complete clinical session
- All 29 v0.6 clinical PlayMode tests pass unchanged in the combined project. This includes `ScenarioFlowPlayTests.FullSession_CompletesWithTraceableDebrief`, which runs the ER scenario from introduction to debrief.
- In the hospital, the exe enters Room 01 and starts `ER_CHEST_PAIN_001`. A patient action is completed (`raise_arm(left)` Completed).
- A full clinical case was **not** played through inside the hospital exe. The standalone `MedSim_ER` scene and its tests cover the full case.

## Stage F: complete surgical session
`Surgery_FullScenario_RealInteractions_AnatomyChanges_Assessment` and `SurgerySequencingTests` (all 9 objectives) cover:
- **valid steps:** marker, scalpel, retractor, guide → saw ×2, trials, components, impactor, tray, insert, mallet, stapler;
- **invalid attempts:** wrong tool → `WrongTool`; wrong (left) knee → `WrongSide`; saw before guide → `PrerequisiteMissing`; implant 9 mm off → `OutOfTolerance`. Each leaves the anatomy unchanged;
- **visible state changes:** Marked, Resected, TrialFitted, Implanted, Closed;
- **progress:** 9 objective completions credited once each;
- **reset:** `R` and restart restore the states, implant poses and instrument slots;
- **exe:** the same run with 22/24 (92%).

## Stage G: AI integration (mocked / offline)
| Item | Covered by |
|---|---|
| Arabic room selection | `HospitalIntentParserTests` (e.g. «خديني على غرفة العمليات», «رجعني عالغرفة الأولى», «الغرفة التانية»); PlayMode `وين غرفة العمليات؟`; exe |
| English room selection | `HospitalIntentParserTests` ("take me to the operating room", "room one", "back to the corridor") |
| Doctor physical actions | `guide_to_room`, `wait_for_student`, `walk_with_student` through `CharacterActionRouter` (PlayMode); v0.6 action tests |
| Educational explanations / help | `HelpRequest_AnsweredImmediately…`, `HelpAndExplain_FromVoiceRequests`, `Idle_GivesLadderHints…`; exe H/J |
| Tool identification | `PassInstrument_English_Accepted`, `PassInstrument_ArabicToDoctor_RoutedToAssistant`, `GiveMeTheInstrument_Unspecified_AsksWhich`, `SpecificGuide_Resolves` |
| Instrument hand-over | `HandOver_CompletesOnlyWhenPlayerActuallyGrabs`, `HandOver_NotTaken_FailsAfterTimeout`, hospital `Handoff_CompletesOnlyAfterTheStudentReallyTakesTheInstrument`; exe log |
| Supervisor behaviour | 12 `SurgicalSupervisor` tests (silent during correct work, wrong-side safety, ladder hints, max times, ignores non-mechanics events) |
| Final debrief | `Debrief_Bilingual_Grounded_WithDisclaimer`; exe `15` |
| Live paid verification | **Not done.** Only $0.0796 of the authorisation remains, and live calls were not needed to verify the integration. |

## Stage H: failure handling
| Case | Covered by | Result |
|---|---|---|
| Backend offline | Every test and exe run (forced offline); text-only labelled mode | Works, says so |
| Invalid AI output | Proxy: unknown fact ids dropped; ungrounded surgical content → approved text; bad rephrase falls back; meta talk and dosing blocked | Pass |
| Missing instrument | `Missing_Denied`, `NotInCatalogue_Clarify`, `AlreadyHolding_Denied` | Pass |
| Ambiguous command | `AmbiguousGuide_AsksForClarification`; parser "both rooms" → Clarify | Pass |
| Navigation failure | `Navigation_ThroughAClosedDoor_IsReportedAsUnavailable_NotFaked` | Pass |
| Incorrect target | Wrong side, wrong tool (Stage F) | Pass |
| Rejected interaction | Stage F; `RepeatedRejections_Intervene` | Pass |
| Repeated restart | `Restart_GivesFreshIndependentSession`; `SurgerySequencingTests` reset; hospital restart intent | Pass (repeated restarts in one exe run: not run) |
| Actuator failure | `ActuatorFailure_ReportedHonestly`; `SubscriberCrash_DoesNotCorruptSession` | Pass |
| Interrupted conversation | v0.6 interruption tests (`CharacterPlayTests`, `NativeVoiceTests`) | Pass in the clinical scene; **not tested inside the hospital** |
| Room change during speech | Exe: the student entered Room 01 while the doctor was speaking the welcome; leaving the OR interrupts the surgical speech (`InterruptSpeech`) | Observed working; **no dedicated test** |
| Room change during character movement | Not specifically tested. The guide re-plans on each request. | **Not tested** |

## Stage I: regression
EditMode 231, PlayMode 42, proxy 46, validator, contract, mechanics, AI core and integration tests: all as in the totals above.

## Standalone executable (§34)
- **Desktop:** `D:/VRBuilds/MedSimAI_Integrated_Desktop/MedSimAI.exe -hospitalSelfTest <dir>`.
  - Launch, reception welcome, guided walk, Room 02 full run, assessment, corridor, guided walk to Room 01, patient action, leave.
  - Log: `Screenshots/Surgery/FinalIntegration/exe/hospital_selftest.log` and `player.log`.
- **Voice:**
  - Voice input in the exe was **synthetic** (the self-test injects the utterance text into the same interceptor the speech recogniser uses).
  - **No physical human voice test** was performed in the hospital build.
  - Speech output was the offline text-only mode, not live TTS.
- **PCVR:** build success is recorded separately. **No headset was available. Nothing here verifies VIVE Focus 3 behaviour.**

## Visual evidence (§35)
`Screenshots/Surgery/FinalIntegration/exe/`, captured from the built Desktop exe:

| # | File |
|---|---|
| 1 | `01_hospital_entrance.jpg` |
| 2 | `02_doctor_welcome.jpg` |
| 3 | `03_corridor_both_room_entrances.jpg` |
| 4 | `04_doctor_walking_to_room.jpg` |
| 5 | `05_clinical_examination_room.jpg` |
| 6 | `06_clinical_patient_interaction.jpg` |
| 7 | `07_integrated_operating_room.jpg` |
| 8 | `08_surgical_patient_on_table.jpg` |
| 9 | `09_knee_anatomy_closeup.jpg` |
| 10 | `10_instrument_tray.jpg` |
| 11 | `11_student_holding_instrument.jpg` |
| 12 | `12_anatomy_state_change_resected.jpg` (+ `12a_guide_placed_robot_guidance.jpg`) |
| 13 | `13_virtual_implant_interaction.jpg` |
| 14 | `14_doctor_providing_assistance.jpg` (+ `14b_assistant_offers_instrument.jpg`) |
| 15 | `15_surgical_assessment.jpg` |
| 16 | `16_return_to_corridor.jpg` |

GIFs: `gif_doctor_guides_to_or.gif`, `gif_femoral_resection.gif`.

## How to re-run
```
unity command --project-path <project> test run --mode editor
unity command --project-path <project> test run --mode playmode
cd Backend/medsim-proxy && node --test
MedSimAI.exe -hospitalSelfTest <out-dir>        (add -uncapped to disable vSync for timing)
```

## Secret scan (before GitHub delivery)
| Scan | Scope | Result |
|---|---|---|
| Key patterns (`sk-…`, `sk-proj-`, AWS, GitHub, Slack tokens, private keys, bearer tokens) | Every line added since `e4fbd9c` | **0 matches** |
| Exact values of the API key and the proxy client token (UTF-8 and UTF-16), read from the local secrets file without printing them | Working tree (4,796 files), both integrated builds, screenshots and logs | **0 matches** |
| The same exact values | Full `git log -p e4fbd9c..HEAD` | **0 matches** |
