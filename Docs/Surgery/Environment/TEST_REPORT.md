# Operating room verification

Unity 6000.3.29f1 compiled the MedSim AI project and generated the scene, room prefab, nested robot prefab, and navigation data. Verification ran on November 27, 2026 against surgery contract 1.1.0 and the v0.6 integration baseline at `e4fbd9c`.

| Unity suite | Passed | Failed |
| --- | ---: | ---: |
| Environment Edit Mode | 7 | 0 |
| Shared surgery contract Edit Mode | 15 | 0 |
| Existing clinical Edit Mode | 98 | 0 |
| Existing clinical Play Mode | 29 | 0 |

The environment checks covered the 13 shared anchor IDs and components, the patient support plane, rendered mesh and URP Lit material references, screen and fabric rendering inputs, doorway and player collisions, navigation to all four working and staff positions, robot joint and flange motion, and the entry event's player and session filters.

The asset suite contains 68 authored mesh assets and approximately 260,000 triangles before view culling and the robot detail LOD.

## Visual evidence

Six 1600 × 900 Unity URP screenshots were inspected for lighting, readability, framing and visible clipping.

| View | Screenshot |
| --- | --- |
| Overview | [Room and circulation](Screenshots/01-overview.png) |
| Surgical position | [Surgeon view](Screenshots/02-surgeon.png) |
| Patient support | [Operating table](Screenshots/03-table.png) |
| Headwall and carts | [Anesthesia, patient monitor and electrical and gas services](Screenshots/04-equipment.png) |
| Robotic assist | [Articulated cart](Screenshots/05-robot.png) |
| Entrance | [Sliding door and room sign](Screenshots/06-entry.png) |

## Desktop benchmark

Unity 6000.3.29f1 macOS development player, Apple M3 with 24 GB unified memory, Metal, URP, and 1920 × 1080 windowed output. The benchmark used the demo camera in Desktop Mode, disabled VSync and the frame cap, discarded 1.75 seconds after each view change, and recorded 240 frames per view. Mean FPS is the Unity frame-loop rate from unscaled frame intervals. GPU timings are Unity Frame Timing Manager samples. Display presentation rate depends on the compositor and monitor.

| View | Pixels | Frames | Mean FPS | Median frame (ms) | p95 frame (ms) | Median GPU (ms) | p95 GPU (ms) | GPU samples |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 01-overview | 1920 × 1080 | 240 | 571.0 | 1.74 | 1.83 | 4.25 | 4.48 | 240 |
| 02-surgeon | 1920 × 1080 | 240 | 530.2 | 1.89 | 2.00 | 4.29 | 4.61 | 240 |
| 03-table | 1920 × 1080 | 240 | 499.7 | 2.02 | 2.11 | 4.69 | 4.94 | 240 |
| 04-equipment | 1920 × 1080 | 240 | 563.6 | 1.79 | 1.88 | 4.17 | 4.34 | 240 |
| 05-robot | 1920 × 1080 | 240 | 518.0 | 1.92 | 2.05 | 4.49 | 4.65 | 240 |
| 06-entry | 1920 × 1080 | 240 | 674.4 | 1.48 | 1.55 | 3.49 | 3.60 | 240 |
| All viewpoints | 1920 × 1080 | 1440 | 554.2 | 1.82 | 2.06 | 4.26 | 4.83 | 1440 |

[Raw timing samples](Performance/mac-m3-1080p.csv)

## Reproduction

Open the demonstration scene and run the environment suite in **Window > General > Test Runner > EditMode**. To regenerate the six screenshots, choose **MedSim AI > Surgery > Capture Operating Room Viewpoints** or call `MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.Capture` with a graphics-enabled Unity editor. The command accepts an optional output path after `-or-screenshots`.

Build the standalone fixture in batch mode using `-executeMethod MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.BuildDesktopBenchmark -or-player <absolute-path-to-app-or-exe>`. Run the development player with `-desktop -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -operating-room-benchmark <absolute-path-to-CSV>`.

## Remaining verification

The room follows surgery contract v1.1.0. Integrated testing with the patient, tools, staff and existing Desktop/XR rig remains open. The desktop timing sample covers the isolated environment camera. Windows RTX 3050 4 GB and physical VIVE Focus 3 verification requires target hardware.
