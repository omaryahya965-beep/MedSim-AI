# Desktop Mode (keyboard + mouse)

Play the complete MedSim AI experience on a laptop or desktop without a headset. It uses the same scene, clinical engine, Live AI backend, characters, instruments, supervisor and assessment as VR Mode. It is not a separate demo.

## Run it
1. Start the AI proxy:
   ```
   cd "D:\Medical-Test\My project\Backend\medsim-proxy"
   node server.mjs
   ```
2. Run `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe`. It opens full screen at your monitor's native resolution.

## Controls
| Input | Action |
|---|---|
| W A S D | Walk (collides with walls, furniture, people and the closed door) |
| Shift | Walk faster |
| Mouse | Look around (first person) |
| E | Interact: open/close the door, record the ECG, sanitise hands, pick up |
| Left click | Pick up an instrument, then apply it where you aim on the patient |
| Right click | Put the held instrument back on the trolley |
| Hold T | Talk to the person you are looking at (doctor or patient). Native voice: the reply streams back in about 2 s. Holding T while a character speaks interrupts them. |
| M | Microphone mode: push-to-talk (default) → hands-free (just speak) → muted |
| Esc | Pause menu: tablet, subtitles, language, display mode, restart, exit. The mouse cursor is released. |
| F1 / H | Show or hide the controls panel |

A crosshair and a short prompt show what you can do with the thing you are looking at, for example "E — open the door" or "Left click — apply the blood pressure cuff (left upper arm)". The top-left status shows LIVE AI, Listening or Thinking, and who you are talking to.

**Instrument use**
- **Cuff:** pick it up, aim at the patient's upper arm, and left-click to wrap it. The NIBP cycle runs and the monitor updates.
- **Pulse oximeter:** aim at a fingertip and left-click.
- **Stethoscope:** hold the aim steady on a heart or lung site to listen.
- **Wrong site:** applying an instrument where it doesn't belong is recorded as a wrong placement, exactly as in VR. The supervisor coaches after repeated mistakes.
- **ECG:** press E on the ECG cart to record. Looking at the tracing for about 1.5 s, or pressing E again, counts as reviewing it.

## Display mode selection
| Way | Effect |
|---|---|
| Automatic (default) | Try OpenXR for up to 4 s. If a headset display starts, run VR Mode; otherwise run Desktop Mode. |
| Command line | `MedSimAI.exe -desktop` or `MedSimAI.exe -vr` |
| Pause menu → "Display" | Cycles Auto / Desktop / VR. Saved, and applies at the next launch. |

"Initialize XR on Startup" is now **off** for Windows; `DisplayModeManager` starts XR manually instead. Android keeps its original setting.

**Two builds**
| Build | Use |
|---|---|
| `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe` | **Desktop build.** OpenXR is excluded from this build only, so it always starts immediately in Desktop Mode, even when SteamVR is running. |
| PCVR build (OpenXR included, auto-selection) | Use with the VIVE Focus 3. **Measured on this PC:** when SteamVR is the active OpenXR runtime but no headset is connected, SteamVR blocks the app at start-up, before any game code runs. That is SteamVR's behaviour, so use the Desktop build in that situation. |

## Rendering in Desktop Mode
- Native screen resolution (render scale 1.0, borderless full screen).
- HDR, 4× MSAA plus SMAA (high).
- 4096 px main-light shadows with 4 cascades.
- Soft realtime shadows, bloom, V-Sync, 16× anisotropic filtering.

There is no XR eye-texture scaling, foveated rendering or streaming compression. The VR graphics profiles are untouched.

## Implementation
- **`Scripts/XR/DisplayModeManager.cs`:** start-up mode decision and manual XR initialisation.
- **`Scripts/XR/DesktopPlayerController.cs`:**
  - First-person controller on the existing XR Origin; it moves the rig root with its CharacterController.
  - Eye height = simulated head height 1.25 m + the unchanged +0.40 m lift = 1.65 m.
  - Crosshair interaction and HUD.
  - Disables only the XR-specific rig parts: controllers, teleport/turn locomotion and the gaze interactor.
- **`MedicalInstrumentBase.DesktopGrab / DesktopRelease`:** the same placement-zone, snapping, dwell, findings and wrong-placement logic as XR grabbing.
- **`MedSimUI.SetDesktopMode`:** menu, pause, debrief report and subtitles become crisp screen-space panels clickable with the mouse.
- **`PerformanceManager.ApplyDesktopQuality`:** the desktop rendering profile.

Unity's XR Interaction Simulator was evaluated and not chosen. It simulates controllers with keyboard chords, which is awkward for a clinical walkthrough; a dedicated first-person controller feels natural and reuses the same clinical interaction code.

## Verified (2026-11-27)
- **PlayMode tests (`DesktopModePlayTests`, 3 tests):**
  - desktop rig and native rendering settings,
  - cuff application and wrong-site detection through the desktop API, producing the same engine events and BP measurement,
  - the closed door blocks walking and the open door lets the player through.
- **Desktop end-to-end in the Editor with simulated keyboard and mouse input and real Live AI** (`AgentScripts/DesktopE2E.cs`, log and screenshots in `Screenshots/DesktopMode/`):
  - automatic detection chose Desktop;
  - W walked to the door and E opened it;
  - walking through the doorway recorded room entry;
  - live conversation with the patient;
  - left click picked up the cuff; a wrong site twice drew a supervisor intervention; applied to the arm, BP was measured;
  - stethoscope on the mitral area gave heart auscultation;
  - E recorded the ECG and looking at it reviewed it;
  - escalation, discussion and an AI-spoken debrief, with the cursor released for the report.
- **Standalone desktop build:** launched with SteamVR running. It opened full screen at native 1920×1080, selected Desktop Mode, and showed the Live AI welcome with HUD and subtitles (`Screenshots/DesktopMode/B03_standalone_desktop_build.png`).

## Verified in the standalone exe (v0.3, 2026-11-27)

Run: `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe -medsimSelfTest <student-audio>`, with real OS input sent through Win32 `SendInput`. The log is in `%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\selftest\`.

- **Start-up:** Desktop Mode chosen immediately. The build contains no XR loader (`xrLoaderInBuild=0`), so it does not depend on SteamVR or OpenXR.
- **Real keyboard and mouse:**
  - W, D, E, left click, T and Esc all reached the game, and mouse look worked.
  - The player walked 1.26 m and turned 27°.
  - T switched to Listening and opened the default microphone on demand.
  - Esc paused.
- **Frame timing at 1920×1080, 240 Hz panel, v-sync on, RTX 3050 Laptop:**

  | Where | Average | p95 | p99 | Worst frame | GPU time |
  |---|---|---|---|---|---|
  | Corridor | 4.18 ms (239 fps) | 4.19 ms | 4.23 ms | 8.4 ms | 1.54 ms |
  | Room | 4.19 ms (238 fps) | 4.18 ms | 4.27 ms | 21.1 ms | 1.64 ms |

  The single 21.1 ms frame was one stall, not sustained.
- **GPU memory** (`nvidia-smi`, whole GPU): 2054 MiB before launch, at most 2484 MiB while running. That is about **0.43 GB** for MedSim, out of 4096 MiB.
- **Native voice turn:** heard "Are you allergic to any medications?", understood in 1.08 s, first audio at 1.88 s.
- **Acoustic loopback (automated audio, not a human):** a synthetic "Hello Doctor Hassan, nice to meet you" was played through the laptop speakers. The real microphone and hands-free VAD captured 2.67 s, the transcript was correct, and the doctor replied 1.55 s after the clip ended.
- **Screenshots** at native resolution: `exe_corridor.png` and `exe_room.png`. HUD, subtitles and prompts are unchanged.

**Not verified:**
- The laptop microphone with a real human voice. Use the checklist in NATIVE_VOICE.md.
- VR Mode via the new manual XR start-up on the actual VIVE Focus 3. If VR Mode fails to start on the headset, re-enable *Project Settings → XR Plug-in Management → Windows → Initialize XR on Startup* (one checkbox) and rebuild the PCVR build.
