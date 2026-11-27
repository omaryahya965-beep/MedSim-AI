# Operating room verification

This report records the isolated operating-room scene against the master-derived surgery baseline. Desktop and headset testing with the shared clinical and surgery systems is a separate integration run.

## Checks

The `MedSimAI.Surgery.Environment.Tests` Unity Edit Mode assembly verifies the unique anchors, room dimensions, patient support, meshes and materials, missing scripts, player collisions, staff pathfinding, demo asset and lights, and nested robot pivots. Use the project's prescribed Unity editor version, 6000.3.29f1.

## Reproduction

Open `OperatingRoom_Environment.unity` and run the tests from **Window > General > Test Runner > EditMode**. Use **MedSim AI > Surgery > Capture Operating Room Viewpoints** to make the six PNGs. The batch capture entry point is `MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.Capture` and accepts an optional output path after `-or-screenshots`. Use a graphics-enabled editor.

For desktop timing, run Unity in batch mode with `-executeMethod MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.BuildDesktopBenchmark -or-player <absolute-path-to-app-or-exe>`. Launch the development player with `-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -operating-room-benchmark <absolute-path-to-CSV>`. The demo discards 1.75 seconds after each transition, then records 240 frames per camera. The benchmark disables VSync and the requested frame cap. It records frame intervals and any valid Unity GPU timestamps.

The standalone RTX 3050 4 GB and physical VIVE Focus 3 runs require the respective hardware. The expected PCVR frame budget at 90 Hz is 11.11 ms. Report the actual runtime, render resolution per eye, average and p95 GPU and CPU frame time, dropped or reprojected frames, and test duration when those runs are available.

## Current evidence

| Check | Result |
| --- | --- |
| C# compiler preflight | Passed for the authoring code and six test methods using Unity 6000.3.29f1 base API references, NUnit, and package API signatures. |
| Exact Unity Editor startup | Blocked by local licensing. Unity 6000.3.29f1 launched and exited with code 198 before opening the project. It reported `No valid Unity Editor license found. Please activate your license.` |
| Generated scene, prefabs and baked navigation | Pending a successful editor run. |
| Unity Edit Mode tests and visual checks | Pending the generated scene. No Unity screenshots captured. |
| Desktop FPS and GPU frame time | Pending a standalone Unity run. |
| RTX 3050 4 GB and VIVE Focus 3 | Pending access to physical target hardware. |
