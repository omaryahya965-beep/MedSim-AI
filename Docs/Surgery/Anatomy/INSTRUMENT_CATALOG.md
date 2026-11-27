# Instrument Catalog

> **DRAFT — NOT CLINICALLY VALIDATED.** These are original, generic instrument designs, not models of any manufacturer's product. Identifiers come from the contract `InstrumentIds`. **Extras** are proposed ids that are not yet in the contract. Person 4 must add them to `SurgeryIds.cs` before mechanics can use them.

**Conventions (every instrument):**
- The root is at the **grip point** (`GripPoint`, local origin) with scale (1,1,1).
- **+Z** points toward the working tip and **+Y** is up.
- `Tip` is the working tip or blade contact point.
- Every instrument has `InteractionPoint`, `AttachmentPoint` and `ResetAnchor` children.
- Cutting guides also have `SeatPoint` (centre of the bone-facing surface) and `SlotPlane` (saw-slot plane). The impactor also has `StrikePoint`.

**Components on the root:**
- `InstrumentTagBridge`, which adds the contract's `SurgeryInstrumentTag` at runtime (see KNOWN_LIMITATIONS §1);
- `InstrumentAssetInfo` (category, size, anchors, movable parts);
- `Rigidbody` (interpolated, continuous-speculative);
- `XRGrabInteractable` with `attachTransform = GripPoint`, throw on detach off, no dynamic attach.

**Colliders:** one `BoxCollider` per visual part (non-trigger), collected automatically by XRI.

**Movable parts:** each is a child carrying `InstrumentMovablePart`, which documents the pivot, axis and range. Its `SetNormalized(0..1)` sets a pose but implements no gameplay; Person 3 drives the motion.

| Id | Name (EN / AR) | Category | Prefab | Size X×Y×Z (mm) | Mass (kg) | Grip / Tip | Tip local pos (m) | Colliders | Movable parts |
|---|---|---|---|---|---|---|---|---|---|
| `inst.skin_marker` | skin marker / قلم تعليم الجلد | Marking | `Instruments/Prefabs/inst.skin_marker.prefab` | 12×14×138 | 0.015 | `GripPoint` / `Tip` | (0, 0, 0.037) | 5 box | `Cap` Translate 0…0.05 |
| `inst.scalpel` | scalpel / مشرط | Cutting | `Instruments/Prefabs/inst.scalpel.prefab` | 13×3×158 | 0.02 | `GripPoint` / `Tip` | (0.006, 0, 0.078) | 14 box | — |
| `inst.retractor` | retractor / مبعد | Retraction | `Instruments/Prefabs/inst.retractor.prefab` | 16×31×223 | 0.2 | `GripPoint` / `Tip` | (0, -0.026, 0.115) | 2 box | — |
| `inst.cutting_guide_femoral` | femoral cutting guide / دليل قطع الفخذ | Guide | `Instruments/Prefabs/inst.cutting_guide_femoral.prefab` | 62×26×163 | 0.35 | `GripPoint` / `Tip` | (0, 0.003, 0.12) | 9 box | — |
| `inst.cutting_guide_tibial` | tibial cutting guide / دليل قطع الساق | Guide | `Instruments/Prefabs/inst.cutting_guide_tibial.prefab` | 56×26×163 | 0.33 | `GripPoint` / `Tip` | (0, 0.004, 0.12) | 9 box | — |
| `inst.oscillating_saw` | oscillating saw / منشار هزّاز | PoweredCutting | `Instruments/Prefabs/inst.oscillating_saw.prefab` | 42×154×159 | 1.3 | `GripPoint` / `Tip` | (0, 0.03, 0.185) | 6 box | `Pivot_Blade` Rotate -4…4<br>`Trigger` Translate 0…0.008 |
| `inst.drill` | surgical drill / مثقب جراحي | PoweredDrilling | `Instruments/Prefabs/inst.drill.prefab` | 42×154×174 | 1.2 | `GripPoint` / `Tip` | (0, 0.03, 0.216) | 6 box | `Pivot_Chuck` Spin 0…1800<br>`Trigger` Translate 0…0.008 |
| `inst.mallet` | mallet / مطرقة | Impaction | `Instruments/Prefabs/inst.mallet.prefab` | 94×41×234 | 0.6 | `GripPoint` / `Tip` | (-0.047, 0, 0.17) | 4 box | — |
| `inst.impactor` | impactor / دافع | Impaction | `Instruments/Prefabs/inst.impactor.prefab` | 40×40×254 | 0.45 | `GripPoint` / `Tip` | (0, 0, 0.153) | 4 box | — |
| `inst.needle_driver` | needle driver / ماسك الإبرة | Suturing | `Instruments/Prefabs/inst.needle_driver.prefab` | 65×6×147 | 0.06 | `GripPoint` / `Tip` | (0, 0, 0.117) | 7 box | `Pivot_JawUpper` Rotate 0…12<br>`Pivot_JawLower` Rotate 0…-12 |
| `inst.skin_stapler` | skin stapler / دبّاسة الجلد | Closure | `Instruments/Prefabs/inst.skin_stapler.prefab` | 22×58×128 | 0.12 | `GripPoint` / `Tip` | (0, 0, 0.083) | 4 box | `Pivot_Trigger` Rotate 0…14 |
| `inst.forceps` **(extra)** | Adson forceps (toothed) / ملقط أدسون مسنّن | Grasping | `Instruments/Prefabs/inst.forceps.prefab` | 12×8×150 | 0.03 | `GripPoint` / `Tip` | (0, 0, 0.09) | 2 box | `Pivot_ArmL` Rotate 0…2.5<br>`Pivot_ArmR` Rotate 0…-2.5 |
| `inst.suction` **(extra)** | Yankauer suction / شفّاط يانكاور | Suction | `Instruments/Prefabs/inst.suction.prefab` | 14×38×270 | 0.03 | `GripPoint` / `Tip` | (0, -0.028, 0.135) | 3 box | — |

**Virtual interaction categories:**
- **Marking:** skin marker.
- **Cutting:** scalpel.
- **Retraction:** Hohmann retractor.
- **Guide:** femoral and tibial cutting blocks; seat with `SeatPoint` on `GuideTarget_<instrumentId>`.
- **Powered cutting and drilling:** oscillating saw and drill.
- **Impaction:** mallet and impactor.
- **Suturing:** needle driver.
- **Closure:** skin stapler.
- **Extras:** grasping (Adson forceps) and suction (Yankauer).

**Tray:** `InstrumentTray.prefab` is a 0.60 × 0.44 m tray, top at y = 0.0145 m. It has one `ANCHOR_InstrumentSlot_<id>` (`SurgeryAnchor`) per instrument; instruments lie with +Z along the tray +X. `InstrumentTray_Populated.prefab` nests every instrument at its slot, kinematic, for display.

**Not built:**
- **`robot.arm`:** Person 1 already ships `OrthopedicAssistRobot.prefab`. The contract's optional robot tag can be added to it.
- **Measuring and alignment instruments** (rods, sizers): no objective in scenario v0.1 needs them.
