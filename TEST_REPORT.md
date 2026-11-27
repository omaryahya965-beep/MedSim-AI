# Test Report — Phase 2 (2026-11-26)

## Automated results (final run)
| Suite | Result |
|---|---|
| Unity EditMode (`Assets/MedSimAI/Tests/EditMode`) | **45 / 45 passed** |
| Unity PlayMode, real `MedSim_ER` scene (`Tests/PlayMode`) | **6 / 6 passed** |
| Proxy (`node --test Backend/medsim-proxy/server.test.mjs`, no network) | **10 / 10 passed** |
| Unity console | **0 errors**, compilation OK |

The 39 Phase 1 tests are preserved and still pass.

### New Phase 2 coverage
- **Humanoid models:** valid Humanoid avatar; required ARKit and viseme blend shapes present; all garments skinned; metric height 1.55–1.95 m; under 45k vertices per character.
- **Character animation (Play Mode):** blink within 7 s, discomfort expression active, visemes driven while speaking.
- **Examination zone alignment (Play Mode):** 10 zones; chest zones near the chest; mitral on the patient's left and aortic on the right; finger zone within 5 cm of the index fingertip; zone normals point out of the body.
- **Instrument interactions on the new body:** the Phase 1 full-session test re-runs cuff, oximeter and stethoscope detection and placement against the MPFB patient.
- **Server-side grounding:**
  - Differently phrased onset questions return the same approved fact.
  - Injected or unknown fact IDs are dropped.
  - Client-supplied facts are ignored.
  - Diagnosis, numbers and dosing are blocked in free text.
  - Version mismatch is refused.
  - Arabic is rendered from the translation file.
- **Client grounding:**
  - AI intent produces approved text only.
  - AI-only recognition is tagged and shown in the debrief.
  - Unsafe supervisor replies are replaced.
  - Investigation and escalation intents go through the scenario engine.
- **Arabic:**
  - The translation file is separate, marked draft (not approved) and covers all facts.
  - The shaper produces correct contextual forms and the lam-alef ligature.
  - There is no runtime machine translation.
- **AI fallback, session reset and assessment integrity:** the Phase 1 tests still pass.

## Visual verification (screenshots inspected, `Screenshots/Phase2/`)
Captured: 01 student viewpoint, 02 doctor close-up, 03 patient face and reclining pose, 04 bed area, 05 instruments on patient, 05b ECG cart, 06 monitor, 07 conversation, 08 debrief.

Problems found and fixed before declaring the visual pass complete:
- Lab-coat alpha holes, then over-inflation (wrong units).
- The patient looked too young; brown hair changed to salt-and-pepper.
- Unskinned clothes (assets were added before the rig).
- Wrong Humanoid auto-mapping.
- Feet poking through the blanket.
- Badge position.
- **Eyes culled** because the bind-pose bounds didn't cover the reposed head; bounds are now fitted.
- Gaze toward the floor while the headset was idle; gaze height is now clamped.
- Mirrored chart and gas labels.
- Missing glyphs (✓✗).
- Monitor label overlap.

## Not verified: requires the physical VIVE Focus 3
Comfort, tracking, grab feel and hand alignment, haptics, push-to-talk, microphone, text readability, and **frame rate**. No FPS figure is claimed.

## Not verified: requires API credentials
Live proxy calls to OpenAI (chat, STT, TTS), real latency, and Arabic speech quality.

## Not verified: requires a medical professional
All clinical content and the Arabic translation (see CLINICAL_REVIEW.md).

## Not run
Android APK build (deferred per instructions) and Windows dictation with a real microphone.

---
# Phase 3 (2026-11-26)

## Automated results
| Suite | Result |
|---|---|
| Unity EditMode | **52 / 52** (45 kept + 7 new) |
| Unity PlayMode, real scene | **13 / 13** (6 kept + 7 new) |
| Proxy (`node --test`) | **10 / 10** |
| Console | 0 errors |

All 61 Phase 2 tests still pass. New coverage:
- **Player height:**
  - +0.40 m default lift applied on Camera Offset, with the rig root on the floor.
  - Head and both controllers sit under Camera Offset.
  - Clamping to 0–1 m, seated mode, persistence and reset.
  - The lift survives teleport, recentring and an XR Origin offset reset.
  - The environment and characters are never rescaled.
  - Instruments remain within interaction range at maximum lift.
- **Graphics:**
  - Profile ordering and VR-safe settings (4× MSAA, LDR, render scale ≤ 1.1).
  - Android never receives PC settings.
  - The VR render assets have no SSAO and no depth/opaque copies.
  - Runtime profile switching uses cloned assets and the per-light shadow policy.
- **XR render settings:** the OpenXR loader and the Focus 3 profile are enabled on Standalone and Android, with Single-Pass Instanced on PC.
- **Textures and materials:** anisotropic + trilinear filtering on textures; alpha-to-coverage on hair, lashes and brows.

## Measurements (real, desktop proxy; **not headset FPS**)
Stereo-sized off-screen render, 1788 px per eye × scale, Unity Editor, XR suspended, RTX 3050 laptop. Median GPU times:

| Configuration | GPU time |
|---|---|
| Original PC asset, Balanced (HDR + MSAA4 + SSAO + 3 soft shadows) | 261 ms |
| VR asset, HDR + MSAA4 | 166 ms |
| VR asset, LDR + MSAA4, 3 soft shadows | 68 ms |
| **Final Balanced (scale 1.0)** | **5.7 ms** |
| **Final High (scale 1.1)** | **6.6 ms** |
| Scale 1.2 | 71–95 ms (rejected) |
| **Final Performance** | **~4–5 ms** |

## VBS log evidence (in-headset session, before Phase 3)
- Auto preset = Performance (GPU score 342).
- Encoder 1152×1152 per eye.
- Foveated compression: Aggressive.
- Bitrate 10–20 Mbps.
- App frame rate about 18–25 FPS (Unity Editor).

## Visual checks (desktop, `Screenshots/Phase3/before` vs `after`, 2560×1440)
Inspected: complete room, student eye level at 1.65 m (assumed 1.25 m tracked + 0.40 m lift), doctor face, patient face, instruments, monitor, tablet, bed area.

Found and fixed:
- The new fabric normal map was too strong; reduced.
- Stiff fingers; now relaxed curl.

These checks are **desktop renders only**. Headset clarity is not verified by them.

## Requires the physical headset
Perceived pixelation and blockiness after the VBS settings change, real FPS of the built player, comfort at +0.40 m, grab alignment, haptics, and microphone conversation.

---
# v0.2 — Complete redesign + Live AI (2026-11-27)

## Automated results (final runs)
| Suite | Result | Notes |
|---|---|---|
| Unity EditMode | **72 / 72** | 50 previous tests kept; 22 new (supervisor, states, reasoning/escalation, no pre-recorded dialogue, character controller/viseme mapping) |
| Unity PlayMode (real `MedSim_ER` scene) | **15 / 15** | 13 previous tests (2 character tests updated for the Rocketbox models), plus 2 new: unreachable-AI fallback, session restart |
| Proxy (`node --test server.test.mjs live.test.mjs`) | **23 / 23** | 10 previous + 13 new: grounding, numbers (EN/AR), concepts, persona isolation, state, debrief, rate limits, budget cap, secret redaction |
| Live AI tests | **A–H pass** | See [LIVE_AI_TEST_REPORT.md](LIVE_AI_TEST_REPORT.md): 163 real requests, about $0.18 |
| Compile / console | 0 errors | |

**Tests removed on purpose:** `VoiceCoverageTests` (2). They asserted that pre-recorded voice files existed, which contradicts the v0.2 requirement that no pre-recorded dialogue exists. They are replaced by `NoPrerecordedCharacterDialogue_InProject`.

## Visual verification
- Before/after renders from the same camera positions: `Screenshots/CompleteRedesign/before` and `/after` (A00–A23).
- Corridor set: C00–C09.
- Characters: A07–A11 and P10–P14.
- Live session: `/live` (G01–G08, E01, F01).

All were reviewed. The environment and character agents each did review→fix passes; the remaining issues are listed in KNOWN_LIMITATIONS.md.

## Build
- **PCVR Windows build:** `D:\Medical-Test\Builds\MedSimAI_PCVR_v0.2\MedSimAI.exe`. Built successfully: 0 errors, 306 MB.
- **Secret scan:** `tools/scan-build-for-secrets.mjs` found no provider key, no client token, no `sk-` pattern and no `OPENAI_API_KEY` reference in the build (208 files) or in `Assets/`. The real key appears 0 times in git history.
- **Not verified:** the built player stops at OpenXR instance creation on this PC (SteamVR running, no headset connected), so its runtime was not observed. Test it with the headset connected.

## Requires the physical headset
- Comfort
- Frame rate
- Hand and controller alignment and grab poses
- Physically opening the door with the lever
- Headset microphone STT quality
- Spatial audio and lip-sync perception
- Text readability
- +0.40 m height (unchanged and still unit-tested)

**APK:** not built, as instructed.

---
# Desktop Mode (2026-11-27)
| Suite | Result |
|---|---|
| Unity EditMode | **72 / 72** |
| Unity PlayMode | **18 / 18**, including 3 new `DesktopModePlayTests`; the existing tests keep the legacy rig via `DisableAutoDesktop` |
| Desktop end-to-end with simulated keyboard/mouse input and real Live AI | Pass — see DESKTOP_MODE.md and `Screenshots/DesktopMode/desktop_e2e_log.txt` |
| Standalone desktop build `D:/VRBuilds/MedSim_Desktop/MedSimAI.exe` | Built with 0 errors; launches full screen at native 1920x1080 in Desktop Mode with Live AI, even with SteamVR running; secret scan clean |
| PCVR build (manual XR start-up + automatic fallback) | Built with 0 errors; secret scan clean. **Not run with the headset.** |

**Root cause of the earlier "player stalls without headset":** SteamVR, registered as the active OpenXR runtime, blocks the OpenXR plugin's start-up queries when no headset is connected. This was verified by launching the build with no OpenXR runtime available (`XR_RUNTIME_JSON` pointed at a missing file), which started normally.

---
# v0.3 — Native Realtime voice and final polish (2026-11-27)
| Suite | Result |
|---|---|
| Unity EditMode | **78 / 78**. New `NativeVoiceTests` (6): streaming buffer, framed header split across chunks, cancel, voice-turn tracking, ECG via voice, dosing guard |
| Unity PlayMode | **19 / 19**. New `XrRecoveryPlayTests`: a pending XR start-up from the previous launch leads to Desktop with a notice, and the flag is cleared |
| Proxy (`node --test`) | **25 / 25**. New: no-speech audio gets no reply, no history and no audio; reply clean-up |
| Native voice end-to-end in Unity, real API (`AgentScripts/NativeVoiceE2E.cs`) | Pass. 10 turns EN and AR; transcripts and subtitles correct; facts, ECG and escalation tracked; STEMI not confirmed (approved fallback); interruption stops speech in one frame; **0 spurious captures** in a hands-free echo soak with the real microphone and speakers |
| Standalone Desktop exe self-test (`-medsimSelfTest`, real OS input through SendInput) | Pass. All keys and mouse received; 239 fps; voice turn first audio 1.88 s; **acoustic loopback through the real microphone**: heard correctly, reply 1.55 s after the clip ended. See DESKTOP_MODE.md |
| Desktop build `D:/VRBuilds/MedSim_Desktop/MedSimAI.exe` | Built with 0 errors; OpenXR loader excluded (`xrLoaderInBuild=0`) |
| PCVR build `D:/Medical-Test/Builds/MedSimAI_PCVR_v0.2/MedSimAI.exe` | Built with 0 errors. With SteamVR 2.18.4 and VIVE Streaming running, it reached "Display mode: VR (headset display running)", responsive, no hang. **Not checked by a person in the headset.** |

Visual polish was checked on screenshots in `Screenshots/Polish/`:
- Legs, jeans and shoes are removed, so nothing shows through the blanket and there are no dark patches.
- The blanket is a procedural drape generated from the pose, with a folded top sheet.
- The wrist twist is fixed and the elbow is slightly flexed.
- Corridor signage is legible from 2 m, and the corridor and room are evenly lit.

Lip sync is calibrated on the runtime voices.

**Human checks still needed:**
- the microphone with your own voice (checklist in NATIVE_VOICE.md);
- the VIVE Focus 3 in the headset;
- Arabic pronunciation by a native speaker;
- clinical review.

---
# v0.4: Arabic, complete speech, Arabic transcripts, VR movement (2026-11-27)
| Suite | Result |
|---|---|
| Unity EditMode | **98 / 98**. New `ArabicAndAudioTests`: 12 language commands, 4 non-commands, mixed-language detection, right-to-left preparation (English and numbers survive TMP reversal), playhead-based completion with the creation pre-read, Arabic intervention and discussion localisation |
| Unity PlayMode | **21 / 21**. New `VRLocomotionPlayTests`: left stick moves, right stick turns and teleports, settings persist, collision radius; Desktop height unchanged by the VR height. Height tests updated to +0.55 m |
| Proxy | **30 / 30**. New: Arabic reply to an Arabic question with the app in English (regeneration, then approved Arabic fallback); commands without any AI call; Arabic lock; translated-transcript case; Arabic director-line fallback; stale cancelled-response events never end the next line |
| Arabic live harness (real API, automated audio) | **13/13 correct language, 13/13 complete audio**, including a 92-word Arabic debrief |
| Arabic in the Unity client (`AgentScripts/ArabicE2E.cs`) | Pass; **0/10 lines cut short**; hands-free false alarm does not cut speech; a real barge-in does |
| Builds | Desktop and PCVR rebuilt at the same paths, 0 errors; secret scans clean |

Details and the manual checklist: ARABIC_AUDIO_VR.md.

---
# v0.5: Conversation panel and embodied AI (2026-11-27)
| Suite | Result |
|---|---|
| Unity EditMode | **98 / 98** |
| Unity PlayMode | **24 / 24**. New: `EmbodiedActionPlayTests` (arm raise with the cuff following, rest, sit up; doctor safe spots), `ConversationPanelPlayTests` (VR panel: long Arabic and English inside their rows, no overlap) |
| Proxy | **32 / 32**. New: echo filtering; action validation (accepted / denied / clarify), results reported back |
| Conversation panel screenshots plus programmatic checks (`AgentScripts/ConversationUiShots.cs`) | 6/6 cases inside their rows, no overlap; long Arabic pages 1/2 → 2/2 with the speech |
| Mocked embodied actions (`AgentScripts/ActionShots.cs`) | 15/15 completed; cuff drift 0.000 m during arm movement |
| Natural instructions, real API (`tools/action-harness.mjs`) | **23/23** |
| Spoken instructions, real Realtime voice (`AgentScripts/ActionVoiceE2E.cs`) | 2/3 executed; 1 misheard by the mini speech model |
| Builds | Desktop and PCVR rebuilt, 0 errors; secret scans clean; v0.4 builds backed up |

The standalone exe was **not** re-run with the self-test this round: someone was using this PC during testing. Details: EMBODIED_AI_AND_CONVERSATION_UI.md.


---
# v0.6: Embodied intelligence (2026-11-27)
| Suite | Result |
|---|---|
| Unity EditMode | **98 / 98** |
| Unity PlayMode | **29 / 29**. New `EmbodiedQueuePlayTests` (5): multi-action order and a rejected unsafe step; interruption; clarify doesn't move; nearest arm on both sides; deep breath, then breathe normally, then deterioration returns to rest; doctor to the other side, then observing from a distance |
| Proxy | **34 / 34**. New: multi-action, nearest arm, an unsafe step blocks the sentence, new doctor actions, a symptom question is not an action, arm-verb correction |
| Natural instructions, real API (`tools/action-harness-v06.mjs`) | **17 / 17** |
| Spoken instructions, real Realtime voice (`tools/voice-action-check.mjs`) | 2/2 correct (raise arm; history question with no action). The sit request was misheard, and the patient asked for clarification instead of guessing |
| **Standalone Desktop exe self-test** | 6/6 mocked actions Completed (cuff 0.000 m on the raised arm). A spoken Arabic instruction was executed. 214 fps during actions (p95 8.33 ms); action layer 0.021 ms per frame |
| Evidence | 10 GIFs and stills in `Screenshots/Embodied/`; realism before/after in `Screenshots/Realism/` |
| Builds | Desktop (`D:/VRBuilds/MedSim_Desktop`) and PCVR (`D:/Medical-Test/Builds/MedSimAI_PCVR_v0.2`) rebuilt, 0 errors; secret scans clean; v0.5 builds backed up |

Three bugs were found by these tests and fixed:
- an action in the same frame as controller creation;
- the speech model's raise→extend mislabel;
- the far arm crossing the chest.

During the exe run a person was speaking near the PC: some transcript lines are real speech, not the synthetic clips. One injected clip got "no speech" once, then passed on retest. **VR is not headset-verified.** Details: EMBODIED_AI.md.
