# Test report (actual results)

Run on 2026-11-27 in a Linux cloud workspace **without Unity**, on `feature/surgery-ai-integration` after merging `feature/surgery-complete` (v0.6 + contract 1.1 + PR #1). Command: `Tools/OffUnityTests/run-all.sh`.

| Suite | Result | What it covers |
|---|---|---|
| Surgery engine-free C# (`Tools/OffUnityTests/Surgery`) | **63 passed, 0 failed** | Contract ids and bus (ordering, isolation, subscriber failures, unknown ids), scenario validation and isolation from the chest-pain scenario, supervisor (silent when correct, grace and self-correction, safety, repeats, idle ladder, help, Arabic, max/cooldown, ignores non-mechanics events), assessment (perfect run, verbal/AI claims earn nothing, mock not credited, safety observations, tolerance/not-assessed, prerequisites, incomplete session, hint counts, bilingual debrief), action registry and parser (all supported phrasings, clarification, routing to assistant, availability, policy), session core (full run, hand-over only on verified grab, hand-over timeout, actuator failure, restart isolation, help/explain, language switch, crash safety, invalid scenario) |
| Clinical regression (`Tools/OffUnityTests/Clinical`) | **52 passed, 0 failed** | The project's existing `ClinicalEngineTests` and `LiveAndSupervisorTests`, unchanged, compiled with the real clinical code |
| Compile check (`Tools/OffUnityTests/CompileCheck`, `-p:IncludeP4Unity=true`) | **Build succeeded** | Shared + all Person 4 Unity-side scripts type-checked against a UnityEngine/TMP/UI/InputSystem/XR/Networking stub. Catches typos and API misuse only |
| Proxy (`node --test`) | **46 passed, 0 failed** | 34 existing clinical-proxy tests (incl. v0.6 additions) + 12 surgical (scenario load and isolation, deferred help/actions, intent constraints, grounding fallback, language command, say grounding, debrief facts, context sanitising, meta/dosing block, HTTP auth and route isolation). Fake AI client, no spending |

## Not run here (needs Unity or hardware)

- Unity Test Runner EditMode/PlayMode, including Person 1's `OperatingRoomTests` and the existing clinical PlayMode tests.
- Scene generation (`MedSim AI → Surgery → Build All Surgery Scenes`), Play Mode walkthrough, desktop and PCVR builds, standalone exe test.
- Live AI voice in Arabic and English (needs proxy key and budget authorisation).
- VIVE Focus 3 headset verification.

## Manual checklist (in Unity)

1. Build All Surgery Scenes → `LAST_SCENE_BUILD.txt` lists ✓/✗ per contract prefab.
2. Play `ScenarioSelect` → Arabic toggle → Clinical starts ER → F10 returns → Surgical starts the OR.
3. OR (mock): the welcome is spoken, the objective panel shows step 1/9, N advances, W gives a hint after ~4 s, S gives an immediate safety stop, "Sami, pass me the scalpel" → assistant walks, offers, G completes the hand-over, H/J work offline, Finish → debrief with mock warning, Restart → fresh session, Return to menu.
4. With the proxy online: speak Arabic and English, say «احكي عربي», interrupt a character mid-sentence.
5. Repeat 2–4 in VR (A/X push-to-talk, controller rays on the menu, hold the left menu button to go back).
