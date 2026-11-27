# VR Setup — HTC VIVE Focus 3 (PCVR via VIVE Business Streaming + SteamVR)

## 1. Root cause of the pixelation (measured on this PC, 2026-11-26)
Evidence comes from VBS's own files: `C:\ProgramData\HTC\ViveSoftware\ViveRR\Log\rrServer_*.txt`, `RRServer\QualityPreset.json` and `serverSetting.setting`.

| Layer | What was happening | Effect |
|---|---|---|
| **VBS quality preset** | `[QualitySelector] Use AUTO(PERFORMANCE) preset, GpuSpeed: 342`. VBS's own benchmark scored the RTX 3050 laptop at **342**, just under the 350 threshold, so Auto picked its *lowest* Focus 3 tier. | Stream input **1536×1536** per eye |
| **VBS encoder** | `The encoder resolution is 1152 x 1152` on every session | Video sent to the headset at **1152×1152 per eye**, upscaled on the headset → **visible pixels / soft image** |
| **VBS compression** | `FovealCompressor … Preset: Aggressive`; bitrate changed dynamically between **10 and 20 Mbps** | **Blocky / square compression artefacts**, worst in the periphery and on fine detail |
| **App frame rate** | `Low Driver FPS` warnings, ~18–25 FPS average, while running **inside the Unity Editor** | VBS lowers quality further; judder |
| Unity render | XR eye texture **1788×1788**, 4× MSAA, Single-Pass Instanced | Already higher than what VBS transmits. **Not** the main cause |

So the visible pixels and blocks are mainly **streaming resolution and compression**, made worse by **low frame rate**. They are not caused by Unity's render resolution or the textures. Aliasing (jagged edges) is a separate, smaller factor, addressed in Unity (section 3).

## 2. Manual settings to change (you need to do these)
### VIVE Business Streaming (PC console → Settings → Graphics). Labels may differ slightly by version.
1. **Quality / Resolution preset: change from *Auto* to *High*.** This is the 2048×2048 / 35 Mbps preset. Auto chose *Performance* because the GPU score of 342 is just under the 350 threshold. If frame timing stays comfortable, try *Ultra* (3072×3072 / 50 Mbps), but it is likely too heavy for an RTX 3050 laptop.
2. **Foveated compression / foveated encoding: set to *Off* or *Conservative*** (currently *Aggressive*). This is the main source of square blocks in the periphery.
3. **Bitrate: set to the maximum your Wi-Fi sustains** (35 Mbps or more), not dynamic/auto, if your version allows it.
4. Use a **dedicated 5 GHz / Wi-Fi 6 router**, with the PC on Ethernet and the headset in line of sight. Low link quality forces bitrate down.
5. Keep the refresh rate at **90 Hz** if frame timing allows. Otherwise 75 Hz gives VBS more headroom per frame.

### SteamVR (Settings → Video)
6. **Render Resolution → Custom → 100%**, and the per-application value for MedSimAI → **100%**. SteamVR currently uses *Auto* (its benchmark suggested 136%). MedSim's own High-Quality profile does any supersampling, so keep SteamVR at 100% to avoid double scaling.
7. Turn off **Motion Smoothing** if VBS reprojection is active; using both causes artefacts.

### Laptop
8. Plug in AC power and set Windows to **Best performance**. In the NVIDIA Control Panel, set *Manage 3D settings → Preferred graphics processor* to **High-performance NVIDIA** for `MedSimAI.exe`, SteamVR and VBS.

## 3. Changes made in Unity (Phase 3)
- **Measured cause found in Unity:**
  - The template "PC" URP asset was configured for desktop: SSAO at full stereo resolution, forced depth and opaque copies, high soft-shadow quality, GPU Resident Drawer.
  - HDR combined with 4× MSAA at stereo resolution measured **166 ms GPU**, a VRAM cliff on this 4 GB laptop GPU.
  - Three soft realtime shadow lights together measured **68 ms**.
- **Fixes:**
  - New dedicated **VR render assets** (`Assets/MedSimAI/Settings/MedSim_PCVR_RP` / `MedSim_Standalone_RP`): Forward path, no SSAO, no depth/opaque copies, LDR, 4× MSAA.
  - Room lights fully **baked** (directional lightmaps). Only the exam spot casts a realtime shadow, for contact shadows on the characters.
- **Texture filtering:** BC7 (high-quality) compression on PC, trilinear filtering, 8× anisotropic on environment textures and 4× on characters. ASTC 6×6 on Android.
- **Alpha-to-coverage** on hair, eyebrows and eyelashes, so their edges are MSAA-smoothed instead of jagged cut-outs.

### Graphics profiles (Comfort & Graphics menu, saved per device)
GPU times are a desktop proxy: a stereo-sized off-screen render of the student's view at 1788 px per eye × render scale, measured in the Unity Editor with XR suspended. They are **not** headset FPS.

| Profile | Render scale | MSAA | Realtime shadow | Measured GPU (desktop proxy) |
|---|---|---|---|---|
| Performance | 0.9 (PC) / 1.0 (Android) | 4× | exam spot, hard | ~4–5 ms |
| **Balanced (default PCVR)** | 1.0 | 4× | exam spot, soft | ~5.7 ms |
| High Quality | 1.1 | 4× | exam spot, soft | ~6.6 ms |

A render scale of 1.2 was rejected: it measured 71–95 ms (VRAM cliff). Android always uses Performance; PC settings are never applied to the standalone build.

## 4. Player height
This uses the same method as VR Target Arena's `PlayerHeightOffset`. XR Origin's **Camera Offset** (which holds the headset camera and both controllers) is raised every `LateUpdate`, after XR Origin applies its own offset.
- Default lift **+0.40 m**, adjustable from 0 to 1 m in 5 cm steps.
- **Seated mode** adds 0.45 m.
- Both settings are saved on the device (PlayerPrefs).
- Floor tracking stays enabled and the rig root stays on the floor, so teleport and recentring preserve the lift.
- The tracked camera pose is never overridden, and the world is never rescaled.

## 4b. v0.3 changes relevant to VR
- **Voice:**
  - Native voice works the same in VR. Hold **A/X** to talk; the headset microphone ("VIVE Virtual Audio Device") is selected automatically.
  - Interrupt a character by pressing A/X while they speak.
  - Hands-free mode is available in the pause menu.
- **XR start-up recovery:**
  - If OpenXR start-up does not finish within 45 s (seen when SteamVR is running without a headset), the app relaunches itself in Desktop Mode (log: `xr_watchdog.log`).
  - If a launch never finished XR start-up, the next launch starts in Desktop Mode and says why.
  - To force VR, launch with `-vr`; to force Desktop, launch with `-desktop`.
- **Unchanged:** the rig, the +0.40 m height lift and the VIVE Focus 3 profile.
- **Verified without wearing the headset:** with SteamVR 2.18.4 and VIVE Streaming running, the PCVR exe reached "Display mode: VR (headset display running)" with no hang. The image and controls in the headset are **not yet verified by a person**.

## 4c. v0.4: height and thumbstick movement
- **Height:** the default VR lift is now **+0.55 m**. Adjust it from the pause menu row (*Lower −5 / VR height / Higher +5*); it is saved and applies immediately. Desktop Mode stays at +0.40 m.
- **Left stick:** smooth movement where you look (strafe, diagonals).
- **Right stick:** snap turn 45° or smooth turn; push forward to teleport.
- **Collision:** the CharacterController has a 0.2 m radius.
- **Settings:** *Pause → Comfort, movement & graphics*: speed, smooth/teleport-only, turn type, snap angle or turn speed, stick dead zone.
- Bindings come from the XRI starter actions (OpenXR primary2DAxis = VIVE Focus 3 thumbstick). **Not yet tested in the headset.**

## 5. How to test (PCVR)
**Run the built player, not the Editor:** `D:\VRBuilds\MedSim_PCVR\MedSimAI.exe`, with SteamVR and VBS running. The Comfort & Graphics panel shows live frame time. The target is ≤ 11.1 ms (90 Hz).

**Not verified yet:** in-headset image clarity, frame rate and comfort. I could only verify desktop render checks.
