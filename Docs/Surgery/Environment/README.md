# Orthopedic operating room

Open `Assets/MedSimAI/Surgery/Scenes/OperatingRoom_Environment.unity` in Unity 6000.3.29f1. Enter Play Mode and use the left and right arrow keys to switch among six fixed demo viewpoints. The scene uses the project's URP settings. The reusable `OperatingRoom.prefab` is inside `Assets/MedSimAI/Surgery/Environment/Prefabs`. It includes an independently reusable nested `OrthopedicAssistRobot.prefab`.

The integration branch was created from master commit `c1b73d6` at the project owner's direction. `Docs/Surgery/SURGERY_CONTRACT.md` was absent from that baseline. The names below come from the environment brief. Person 4 still needs to confirm the placements, transform ownership, and integration bindings.

## Scale and layout

One Unity unit equals one meter. Room-local zero sits on the floor beneath the patient seat section. The room is 7.60 m wide by 7.80 m deep with a 3.10 m clear ceiling. The 59.28 m² interior excludes the 2.46 × 1.64 m entry vestibule. The width and area use healthcare planning references for larger procedure rooms. The layout is for simulation.

The operating table is 2.09 × 0.51 m at the deck, 0.92 m high at the mattress, and approximately 0.64 m wide at the base and outer hardware. The patient's head faces +Z. The mobile anesthesia and patient-monitor stations occupy the head end. The robot is on the +X side of the foot section, leaving the −X approach for the primary working position. A back table to the west and a Mayo stand near the foot provide instrument staging. The main entry is in the −Z wall.

- **Architecture** includes welded vinyl and coved skirting, flush wall panels and guards, ceiling diffusers and return grilles, an aluminum-framed sliding entry with vision glass, and a vestibule.
- **Clinical equipment** includes a five-section operating table, anesthesia workstation, separate patient monitor, wall room-status display, instrument and Mayo stands, IV stand, mobile storage cart, medical gas and electrical connections, cases and shelving, foot controls, and waste bins.
- **Lighting** includes two articulated optical heads, one shadow-casting surgical beam, a second fill beam, broad room light, and an interior reflection probe.
- **Orthopedic assist cart** includes braked castors, a drive pillar, six separate rotating joints, a parked control display, and an open mechanical flange.

## Provisional integration anchors

Positions are in `OperatingRoom` space unless described as local to a parent. Use +Y as up and +Z as forward for markers. Keep room and robot transforms at an unscaled `(1, 1, 1)`.

| Transform | Position in meters | Forward or intended attachment |
| --- | --- | --- |
| `PlayerSpawn` | (−1.93, 0, −2.79) | Floor-level rig origin faces diagonally into the room |
| `PatientRoot` | (0, 0.92, 0) | +Z toward the head end. Person 2 should align their patient support plane to Y = 0.92 |
| `SurgicalSite` | (−0.14, 1.04, −0.51) | Uses room orientation. Person 2 should replace this placement reference with the appropriate anatomy transform once patient alignment is agreed |
| `SurgeonPosition` | (−1.16, 0, −0.56) | Faces the surgical site horizontally |
| `AssistantPosition` | (−0.84, 0, 0.87) | Faces the surgical site horizontally |
| `InstrumentTable` | (−2.53, 0.985, 0.12) | Surface origin between the trays. Local +Z points toward the room |
| `RobotBase` | (1.99, 0, −0.94) | Root of the nested cart prefab. Rest orientation matches the room |
| `RobotToolMount` | (0, 0, 0.085) in `J6_ToolRoll` space | +Z points out of the flange. Position moves with the arm |
| `MonitorPosition` | (0, 0, 0.056) in `Patient Monitor Display` space | +Z points out from the screen toward the patient and team |
| `RoomExit` | (−1.92, 0, −4.72) | Faces −Z from the vestibule |

The six unparented-by-equipment markers sit in `IntegrationAnchors`. The other markers are bound to their cart, display, or tool flange so they follow placement and movement. Resolve by name under the instantiated room or bind serialized transform references. Avoid assuming they are all direct children of one transform.

## Robot pivots

Use the joint's local axis relative to its serialized rest rotation. The kit supplies parked geometry and broad-phase colliders. Person 3 owns joint limits, reach, collision queries, and motion.

| Parent → child | Local axis |
| --- | --- |
| `RobotBase` → `J1_BaseYaw` | +Y |
| `J1_BaseYaw` → `J2_ShoulderPitch` | +Z |
| `J2_ShoulderPitch` → `J3_ElbowPitch` | +Z |
| `J3_ElbowPitch` → `J4_ForearmRoll` | +X |
| `J4_ForearmRoll` → `J5_WristPitch` | +Z |
| `J5_WristPitch` → `J6_ToolRoll` | +Z |
| `J6_ToolRoll` → `RobotToolMount` | Local +Z is the working-end direction |

## Integration

1. Add one `OperatingRoom.prefab` instance to the shared surgery scene after the contract is agreed. Align its root to the chosen room origin and check the shared scene's ambient lighting against the demo.
2. Move the existing Desktop and XR rig to `PlayerSpawn`. Solid walls, flooring, table sections, doorway leaves, carts, and robot have environmental colliders. Reuse the project's existing `DesktopPlayerController` and `VRLocomotion`.
3. Register only the floor colliders under `Architecture/Walkable Floor` with the rig's existing XRI teleportation system. Use the project's teleport layer settings and landing clearance rules. Move the rig to a point whose controller capsule clears furniture after teleporting.
4. Use the room root's collider-based, child-scoped `NavMeshSurface` for staff. Confirm the shared agent type, radius and height. Rebuild navigation after a layout, collider, or agent-size change.
5. The entry has separately movable `DoorLeafLeft` and `DoorLeafRight` transforms, each with a collider and a carving obstacle. Opening translates the leaves in local X by −0.91 and +0.91 m from their closed positions. Bind opening to the surgery-side desktop or XR interaction. The baked mesh includes the opening and the stationary closed leaves carve it at runtime. The vestibule is a short, bounded transition area.
6. Align the patient and anatomy with Person 2, use the `InstrumentTable` transform for tool staging, and bind the robot from its root through the six joints with Person 3. Confirm working clearances after the patient, staff avatars and tools are installed.

The screenshot tour camera belongs to the demo scene. `MedSim AI > Surgery > Rebuild Operating Room Prototype` regenerates the serialized scene, prefabs, meshes, materials and navigation asset from the Unity Editor source. Treat the Editor generators as the source of truth for future rebuilds.

## Rendering and validation

The meshes are authored in meter coordinates and consolidated by assembly and material. Room geometry uses static-batching and occlusion flags where applicable. Door panels and the robot remain movable. A robot LOD drops fasteners and service details while retaining its silhouette. Small compressed textures carry the surface and screen art. Room fill and the surgical lights use URP lights, and the reflection probe uses a small neutral cubemap. One lamp casts realtime shadows with the desktop URP setting. The current demonstration uses realtime lighting. Any baked lighting or occlusion data for a combined surgery scene belongs to that scene's build and profiling pass.

See [asset sources and licenses](ASSET_LICENSES.md) and the [test report](TEST_REPORT.md).
