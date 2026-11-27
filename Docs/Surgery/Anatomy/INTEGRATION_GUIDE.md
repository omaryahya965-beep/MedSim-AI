# Integration Guide (Person 2 assets)

## Person 1: operating room
- **Patient placement:** parent `Anatomy/Prefabs/SurgicalPatient.prefab` to `ANCHOR_PatientRoot` with a **zero local pose**.
  - The patient's origin is on the support plane, under the mid-point of the hip joints. +Z is toward the head.
  - Your provisional `PatientRoot` (0, 0.92, 0) works as is.
- **Table fit:** the body spans x ±0.251 m, which fits the 0.51 m deck. Along z it runs from **+0.82 m (top of head)** to **−0.92 m (left heel)**, so the deck must cover roughly z −0.95…+0.85 relative to `PatientRoot`.
  - The drapes assume a deck from z −1.04 to +1.05 and hang over its sides to about 0.5 m below the top.
- **Surgical site:** the right knee sits at **(−0.154, 0.398, −0.235) m** from `PatientRoot`.
  - In world terms with your anchor that is about **(−0.154, 1.318, −0.235)**, so the knee is flexed upward.
  - This **replaces** your provisional `SurgicalSite` (−0.14, 1.04, −0.51). Please adjust the light focus, `ANCHOR_StudentOperatingPosition` and the robot reach.
  - The foot rests on the table at about z −0.56.
- **Anaesthesia screen:** the drape includes an optional `ScreenFrame` (bar 0.56 m above the table at z +0.52). If your room has its own screen or IV poles, disable `SurgicalDrape/ScreenFrame`.
- **Reflections:** metallic instruments and implants need a reflection probe in the OR. Without one, URP falls back to the skybox.

## Person 3: mechanics
- **Targets:**
  - Find them via `GetComponentsInChildren<AnatomyTargetStateView>()` (it is the `IAnatomyStateView`) or, at runtime, via the contract's `SurgeryAnatomyTag`.
  - Change visuals only through `IAnatomyStateView.SetState(state)` with the scenario's states. It returns false for unsupported states.
  - `KneeAnatomyStateController.ResetAll()` resets everything (use it for `ResetForNewSession`).
- **Implants:**
  - Seat an implant by matching its root (`PlacementAnchor` = origin) to `PlacementTarget_<implantId>`.
  - Position and angle errors are distances and angles between those two transforms.
  - Leave `showComponentVisuals` off, so the knee does not also draw its built-in copies.
- **Instruments:**
  - The root is the grip; `Tip` is the contact point.
  - Movable parts: `Pivot_Blade` (saw, ±4° about local Y), `Pivot_Chuck` (drill, spin about local Z), `Pivot_JawUpper` / `Pivot_JawLower` (needle driver, about local Y), `Pivot_Trigger` (stapler, about local X), the marker `Cap` (translate +Z), and `Pivot_ArmL` / `Pivot_ArmR` (forceps).
  - `InstrumentMovablePart` documents the axis and range. Drive the parts yourself.
- **Cutting guides:** align `SeatPoint` to `GuideTarget_<instrumentId>` (+Z into the bone). `SlotPlane` marks the saw slot.
- **Colliders:**
  - Instruments: box colliders per part, non-trigger.
  - Implants: convex mesh colliders.
  - Bones, cartilage and menisci: static mesh colliders for contact tests.
  - Targets: trigger boxes. Add layers or contact filters as you need.
- **Desktop:** nothing on the prefabs is VR-only. `XRGrabInteractable` is present for VR; the desktop instrument API can drive the same transforms.

## Person 4: AI, assessment and integration
- **Scene placement:** put `Instruments/Prefabs/InstrumentTray_Populated.prefab` (or the empty tray plus your own spawning) at `ANCHOR_InstrumentTable`, and `Anatomy/Prefabs/KneeImplantSet.prefab` at `ANCHOR_ImplantTable`.
- **Display names:** English and Arabic names are on the tag bridges, from the scenario JSON.
- **Proposed extra ids:** `inst.forceps` and `inst.suction`. Add them to `InstrumentIds` if you want them in scenarios.
- **Contract fix needed:** see PREFAB_CONTRACT "Shared components". Splitting `SurgerySceneTags.cs` makes the tags serializable; the bridges can stay as harmless helpers or be removed later.
- **Credits:** merge the licence lines from [ASSET_LICENSES.md](ASSET_LICENSES.md) into `ASSET_CREDITS.md`. The Z-Anatomy-derived meshes require CC BY-SA 4.0 attribution.
- **Branch base:** `feature/surgery-complete` is based on `c1b73d6` (v0.5), not on the v0.6 baseline `4108aaf`. Please merge or rebase it when integrating. My branch only adds files under my folders.
