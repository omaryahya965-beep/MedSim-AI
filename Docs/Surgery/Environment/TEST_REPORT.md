# Operating room verification

Unity 6000.3.29f1 compiled the MedSim AI project and generated the scene, room prefab, nested robot prefab, and navigation data. The six environment Edit Mode tests passed on November 27, 2026.

| Edit Mode check | Result |
| --- | --- |
| Room scale, ten unique anchors, patient support height and knee reference | Pass |
| Meshes, material and texture bindings, lettering orientation, fabric normals, and script references | Pass |
| Floor, wall, table and door collisions, including a 0.22 m radius CharacterController passing through the open door | Pass |
| Baked navigation between spawn and both staff positions, with the table excluded from walking surfaces | Pass |
| Reusable nested robot prefab, six-pivot hierarchy, flange motion and collision bodies | Pass |
| Demo scene, surgical spotlights, shadow configuration and reflection asset | Pass |

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

The Apple M3 desktop timing run is in progress.

## Reproduction

Open the demonstration scene and run the environment suite in **Window > General > Test Runner > EditMode**. To regenerate the six screenshots, choose **MedSim AI > Surgery > Capture Operating Room Viewpoints** or call `MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.Capture` with a graphics-enabled Unity editor. The command accepts an optional output path after `-or-screenshots`.

Build the standalone fixture in batch mode using `-executeMethod MedSimAI.Surgery.Environment.Editor.OperatingRoomEvidence.BuildDesktopBenchmark -or-player <absolute-path-to-app-or-exe>`. Run the development player with `-desktop -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -operating-room-benchmark <absolute-path-to-CSV>`.

## Remaining verification

The integration contract was absent from the baseline, so the anchor placements require team confirmation before the room joins the shared surgery scene. This desktop sample covers the environment demo camera. Testing the integrated Desktop/XR rig, Windows RTX 3050 4 GB, and physical VIVE Focus 3 requires the shared scene and target hardware.
