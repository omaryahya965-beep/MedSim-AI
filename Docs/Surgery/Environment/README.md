# Orthopedic operating room

Open `Assets/MedSimAI/Surgery/Scenes/OperatingRoom_Environment.unity` or the contract-named `Env_Test.unity` in Unity 6000.3.29f1. In Play Mode, use the left and right arrow keys to switch among six fixed viewpoints. The scenes use the project's URP settings.

The contract entry point is `Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom_Env.prefab`, a variant of the reusable `OperatingRoom.prefab`. The nested `OrthopedicAssistRobot.prefab` can also be used independently. This branch started from `c1b73d6` and incorporated the shared integration contract at `c9d03fe`. [Surgery contract v1.0.0](../SURGERY_CONTRACT.md)

## Scale and layout

One Unity unit equals one meter. Room-local zero sits on the floor beneath the patient seat section. The room is 7.60 m wide by 7.80 m deep with a 3.10 m clear ceiling. The 59.28 m² interior excludes the 2.46 × 1.64 m entry vestibule. The width and area use healthcare planning references for larger procedure rooms. The layout is for simulation.

The operating table is 2.09 × 0.51 m at the deck, 0.92 m high at the mattress, and approximately 0.64 m wide at the base and outer hardware. The patient's head faces +Z. The mobile anesthesia and patient-monitor stations occupy the head end. The robot is on the +X side of the foot section, leaving the −X approach for the primary working position. A back table to the west and a Mayo stand near the foot provide instrument staging. The main entry is in the −Z wall.

- **Architecture** includes welded vinyl and coved skirting, flush wall panels and guards, ceiling diffusers and return grilles, an aluminum-framed sliding entry with vision glass, and a vestibule.
- **Clinical equipment** includes a five-section operating table, anesthesia workstation, separate patient monitor, wall room-status display, instrument and Mayo stands, IV stand, mobile storage cart, medical gas and electrical connections, cases and shelving, foot controls, and waste bins.
- **Lighting** includes two articulated optical heads, one shadow-casting surgical beam, a second fill beam, broad room light, and an interior reflection probe.
- **Orthopedic assist cart** includes braked castors, a drive pillar, six separate rotating joints, a parked control display, and an open mechanical flange.

## Integration anchors

All 13 environment anchors use the exact shared `AnchorIds` values and carry a `SurgeryAnchor` component. Resolve them with `SurgeryAnchor.Find(id)`. Positions below are room-local meters. +Y is up, and +Z is forward.

| Anchor | Position in meters | Orientation and use |
| --- | --- | --- |
| `ANCHOR_OR_Root` | (0, 0, 0) | Floor centre. Room axes and unit scale |
| `ANCHOR_PlayerSpawn` | (−1.93, 0, −2.79) | Floor-level rig origin faces into the room |
| `ANCHOR_RoomEntry` | (−1.92, 0.94, −3.24) | 1.62 × 1.88 × 1.16 m trigger just inside the door |
| `ANCHOR_OperatingTable` | (0, 0.92, 0) | Tabletop centre and support plane. +Z toward the head |
| `ANCHOR_PatientRoot` | (0, 0.92, 0) | Attach Person 2's patient prefab at zero local pose |
| `ANCHOR_InstrumentTable` | (−0.889, 1.098, −1.445) | Mayo tray surface centre with the stand's −13° yaw |
| `ANCHOR_ImplantTable` | (2.89, 0.912, 1.13) | Mobile cart staging surface. Local +Z faces −X |
| `ANCHOR_RobotBase` | (1.99, 0, −0.94) | Floor-level mount with room axes. The demo robot is parented here at zero local pose |
| `ANCHOR_OverheadMonitor` | (3.664, 1.95, 1.61) | Attachment on the wall display. Local +Z faces into the room |
| `ANCHOR_SurgeonObserve` | (1.12, 0, 1.03) | Faces the head of the table from the +X side |
| `ANCHOR_AssistantHome` | (−0.84, 0, 0.87) | Faces the working area from the head side |
| `ANCHOR_AssistantAtInstrumentTable` | (−1.75, 0, −1.48) | Faces the Mayo stand from the west |
| `ANCHOR_StudentOperatingPosition` | (−1.16, 0, −0.56) | Faces the right-knee working area from −X |

Person 2 supplies `ANCHOR_SurgicalSite_Knee_R` in the patient prefab and the instrument slot anchors. The supplemental `RobotToolMount` sits at (0, 0, 0.085) in the flange's local space, with +Z pointing out of the flange. `MonitorPosition` marks the patient monitor screen face. `RoomExit` is at (−1.92, 0, −4.72) and faces −Z.

## Robot pivots

Use the joint's local axis relative to its serialized rest rotation. The original demonstration robot supplies parked geometry and broad-phase colliders. The shared contract's optional tagged `robot.arm` guidance asset is separate. Person 3 owns joint limits, reach, collision queries, and motion.

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

1. Add one `OperatingRoom_Env.prefab` instance to the shared surgery scene. Align its root to the chosen room origin and check the shared scene's ambient lighting against the demo.
2. Move the existing Desktop and XR rig to `ANCHOR_PlayerSpawn`. Bind its root on the entry trigger with `RoomEntryPublisher.BindPlayer(rig.transform)`. Start the shared event-bus session before locomotion. The trigger accepts colliders under the bound rig and emits `player.entered_room` with source `environment` once per session. Solid walls, flooring, table sections, doorway leaves, carts, and the robot have environmental colliders.
3. Register only the floor colliders under `Architecture/Walkable Floor` with the rig's existing XRI teleportation system. Use the project's teleport layer settings and landing clearance rules. Move the rig to a point whose controller capsule clears furniture after teleporting.
4. Use the room root's collider-based, child-scoped `NavMeshSurface` for staff. Confirm the shared agent type, radius and height. Rebuild navigation after a layout, collider, or agent-size change.
5. The entry has separately movable `DoorLeafLeft` and `DoorLeafRight` transforms, each with a collider and a carving obstacle. Opening translates the leaves in local X by −0.91 and +0.91 m from their closed positions. Bind opening to the surgery-side desktop or XR interaction. The baked mesh includes the opening and the stationary closed leaves carve it at runtime. The vestibule is a short, bounded transition area.
6. Align the patient and anatomy with Person 2, use `ANCHOR_InstrumentTable` for tool staging, and bind the robot from its root through the six joints with Person 3. Confirm working clearances after the patient, staff avatars and tools are installed.

The screenshot tour and its local event bus belong to the demo scenes. The shared surgery scene supplies its own event-bus host. `MedSim AI > Surgery > Rebuild Operating Room Prototype` regenerates the serialized scene, prefabs, meshes, materials and navigation asset from the Unity Editor source. Treat the Editor generators as the source of truth for future rebuilds.

## Rendering and validation

The meshes are authored in meter coordinates and consolidated by assembly and material. Room geometry uses static-batching and occlusion flags where applicable. Door panels and the robot remain movable. A robot LOD drops fasteners and service details while retaining its silhouette. Small compressed textures carry the surface and screen art. Room fill and the surgical lights use URP lights, and the reflection probe uses a small neutral cubemap. One lamp casts realtime shadows with the desktop URP setting. The current demonstration uses realtime lighting. Any baked lighting or occlusion data for a combined surgery scene belongs to that scene's build and profiling pass.

See [asset sources and licenses](ASSET_LICENSES.md) and the [test report](TEST_REPORT.md).
