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
