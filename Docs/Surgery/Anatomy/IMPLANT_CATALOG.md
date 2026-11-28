# Implant Catalog

> **Generic educational geometry. Not a medical device and not for manufacturing, implantation or surgical planning.** These are original shapes derived from the virtual bone cuts and resemble no proprietary design. DRAFT — NOT CLINICALLY VALIDATED.

**Frame (every implant prefab):**
- The root is the **seating pivot** (`PlacementAnchor`, at the origin).
- **+Y** is proximal (into the bone for the femoral component; articular side up for the tibial parts).
- **+Z** is anterior and **+X** is lateral (patient's right for this right knee).

**Children:**
- `Visual` (mesh with a convex `MeshCollider`);
- `GripPoint` (bounds centre; the `XRGrabInteractable` attach point);
- `PlacementAnchor` and `ResetAnchor` (both at the origin).

**Root components:** `ImplantTagBridge` (adds the contract's `SurgeryImplantTag` at runtime), `ImplantAssetInfo`, `Rigidbody` and `XRGrabInteractable`.

**Seating:** set the implant root's world pose to `KneeAnatomy_R/ImplantAnchors/PlacementTarget_<implantId>`. The EditMode test `Implants_SeatExactlyOnTheirPlacementTargets` checks that every implant lands on the authored geometry within 1 mm.

| Id | Trial | Seats on | Prefab | Size X×Y×Z (mm) | Placement target (in KneeAnatomy_R) | Local position (m) | Local rotation (quat) |
|---|---|---|---|---|---|---|---|
| `implant.femoral_trial` | yes | `knee_r.distal_femur` | `Instruments/Prefabs/Implants/implant.femoral_trial.prefab` | 66.4 × 61.4 × 50.0 | `ImplantAnchors/PlacementTarget_implant.femoral_trial` | (0.003, -0.009, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `implant.tibial_trial` | yes | `knee_r.proximal_tibia` | `Instruments/Prefabs/Implants/implant.tibial_trial.prefab` | 57.6 × 4.0 × 37.9 | `ImplantAnchors/PlacementTarget_implant.tibial_trial` | (0.009, -0.037, -0.011) | (0.004, 0.922, 0.381, -0.063) |
| `implant.femoral_component` | no | `knee_r.distal_femur` | `Instruments/Prefabs/Implants/implant.femoral_component.prefab` | 66.4 × 61.4 × 50.0 | `ImplantAnchors/PlacementTarget_implant.femoral_component` | (0.003, -0.009, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `implant.tibial_tray` | no | `knee_r.proximal_tibia` | `Instruments/Prefabs/Implants/implant.tibial_tray.prefab` | 67.6 × 41.5 × 37.9 | `ImplantAnchors/PlacementTarget_implant.tibial_tray` | (0.009, -0.037, -0.011) | (0.004, 0.922, 0.381, -0.063) |
| `implant.poly_insert` | no | `knee_r.proximal_tibia` | `Instruments/Prefabs/Implants/implant.poly_insert.prefab` | 55.6 × 10.9 × 36.5 | `ImplantAnchors/PlacementTarget_implant.poly_insert` | (0.009, -0.034, -0.008) | (0.004, 0.922, 0.381, -0.063) |

## How the geometry was made (illustrative)
- **Femoral component and femoral trial:**
  - The bone volume removed by five placeholder cuts: distal 9 mm, posterior 9 mm, anterior 9 mm below the most anterior condylar point, plus two 10 mm chamfers.
  - Only up to 62 mm component height.
  - The articular surface is polished. Two 7 × 13 mm fixation pegs are added.
  - It forms one connected shell, with the anterior flange joining both condyles. The bone-facing side uses the `M_Implant_Back` blasted finish.
  - The trial is the same shape in `M_Trial`.
- **Tibial tray:**
  - The tibial cut outline, inset 1.5 mm, as a 4 mm plate.
  - A Ø12 × 38 mm keel stem with two fins.
  - Titanium-like material.
- **Tibial trial:** the same plate without the keel.
- **Polyethylene insert:**
  - The outline inset 2.5 mm, 10 mm thick.
  - Two 3 mm medial and lateral dishes and a low central eminence.
- **Patellar component:** not built, because the scenario has no patellar resurfacing.

**Visual states:** the knee prefab also contains visual copies (`StateVisuals/ComponentVisuals/*`). They are hidden by default (`showComponentVisuals = false`), because in production Person 3 places the real implant prefabs.
