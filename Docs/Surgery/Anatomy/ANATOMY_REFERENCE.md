# Anatomy Reference

> **DRAFT — NOT CLINICALLY VALIDATED.** The anatomy is educational and generic, not patient-specific. Nothing here is a surgical parameter. An orthopaedic surgeon must review it before training use.

## Sources
- **Knee structures:** [Z-Anatomy](https://github.com/Z-Anatomy) (CC BY-SA 4.0), a curated atlas built on BodyParts3D (DBCLS; database now CC BY 4.0). Z-Anatomy adds the joint soft tissue: cruciate and collateral ligaments, menisci, capsule, fat pad and popliteal ligaments.
- **Patient body:** MakeHuman / MPFB2 system assets (CC0). It's an older male, 1.78 m, generated for this module and independent of the ER patient.
- **Anatomical references used to check plausibility, not copied:**
  - Gray's Anatomy (41st ed.), knee joint chapter;
  - the Z-Anatomy atlas definitions (from Wikipedia, CC BY-SA 3.0);
  - typical adult dimensions: femoral component ML 60–75 mm, tibial baseplate ML 60–80 mm and AP 40–50 mm, patellar tendon 45–50 mm long and 25–30 mm wide, articular cartilage 2–3 mm.

## Structures (44)
- **Bones:** femur shaft, distal femur, tibial shaft, proximal tibia, fibula, patella. Each is closed. The articular zone is inset by the cartilage thickness, i.e. the subchondral surface.
- **Cartilage:** femoral, tibial and patellar. Each is a 2 mm layer over its bone.
- **Menisci:** medial and lateral, plus the transverse ligament and the meniscotibial (coronary) ligaments.
- **Ligaments:**
  - ACL and PCL;
  - MCL (superficial and deep);
  - LCL;
  - the popliteal group (oblique popliteal, arcuate popliteal, popliteofibular);
  - the patellar retinacula and meniscopatellar ligaments.
- **Tendons:** the quadriceps tendon with the distal quadriceps, and the patellar tendon.
- **Soft tissue:** articular capsule, infrapatellar fat pad, suprapatellar bursa.
- **Optional, inactive by default:** gastrocnemius heads, popliteus, pes anserinus (sartorius, gracilis, semitendinosus), hamstrings, iliotibial tract. These are planar sections.
- **Skin and state geometry:**
  - prepped knee skin, the opened skin with wound walls, the site-marking line, the incision line, the closure line and staples;
  - femoral and tibial guide pins;
  - visuals of the five components.

The full id list is in [PREFAB_CONTRACT.md](PREFAB_CONTRACT.md).

## Educational simplifications and what is missing
- **Patellar tendon:** procedural. It is a lofted band from the patellar apex to the tibial tuberosity (50 mm × 30 mm × 6 mm), because Z-Anatomy has no separate patellar ligament object.
- **Knee flexion pose:**
  - The tibial group rotates about a fitted condylar flexion axis, about 2.3 cm proximal to the joint line, chosen to avoid penetration (residual ≤ 0.06 mm).
  - The patella rotates by 0.6 × the flexion angle and is seated about 1 mm above the trochlea.
  - Ligaments, capsule and muscles are blended between bones by distance-weighted linear blend skinning. This is not biomechanics.
- **Fit:** anatomy is scaled ×0.857 (femur length matched to the patient, then 95%) so that soft tissue fits inside the MPFB leg.
- **Skin:**
  - The knee skin was tightened over the bone, to about 10 mm over bone and 6 mm over tendon, and relaxed.
  - The posterior popliteal crease is compressed by MPFB's flexed pose. The deep posterior muscles are therefore shipped inactive.
- **Not modelled:**
  - neurovascular structures (popliteal artery and vein, tibial and common fibular nerves; BodyParts3D has them, not included);
  - plicae;
  - bursae other than the suprapatellar;
  - osteophytes and arthritic changes (the bones are healthy atlas bones, not an arthritic TKA candidate);
  - bone marrow and cancellous bone, except as a texture on cut surfaces.
- **Exposed state:**
  - Shown as a lens-shaped skin window, about 18 cm long and up to 68 mm wide, with subcutaneous wound walls.
  - The anterior capsule, fat pad, retinacula, bursa, quadriceps belly and patellar tendon are hidden.
  - The patella group is retracted laterally under the skin flap, by a pose found by collision search.
  - This is a visual simplification of a medial parapatellar exposure, not a procedure.
- **Resection and implant geometry:**
  - The femoral resection is five planes (distal, anterior, posterior, two chamfers). The tibial resection is one plane.
  - The thicknesses (9 mm distal, 9 mm posterior, 9 mm anterior trochlear, 9 mm tibial) are **illustrative placeholders, not surgical values**.
  - Cuts have no rotation or slope, and nothing is planned or measured.
  - These states demonstrate that the assets can switch. The real sequence and values must come from a reviewed scenario.

## Coordinate conventions
Unity is left-handed, +Y up, 1 unit = 1 m (contract §4).

| Frame | Origin | Axes |
|---|---|---|
| `SurgicalPatient` (= `ANCHOR_PatientRoot`) | Table support plane, under the mid-point of the hip joints | +Z toward the head, +Y up, **patient's right = −X**. Supine, facing +Y. |
| `ANCHOR_SurgicalSite_Knee_R` / `KneeAnatomy_R` | Right knee joint centre, at (−0.154, 0.398, −0.235) m from the patient root | Parallel to the patient frame (no rotation) |
| `FemurRoot` | Centre of the illustrative distal femoral cut | +Y proximal (toward the femoral head), +Z anterior, +X lateral (patient's right) |
| `TibiaRoot` | Centre of the illustrative tibial cut | +Y proximal, +Z anterior, +X lateral |
| `PatellaRoot` | Patella centroid (moves with `PatellaGroup`) | As `FemurRoot` |
| `PatellaGroup` pivot | Tibial tuberosity | Retracted pose: `PatellaRetractedPose` |
| Mesh pivots | Bones at their root anchors; soft tissue at its centroid; resected variants share their intact pivot | Identity rotation in the knee frame |

**Side:**
- The operative side is configurable only by rebuilding; the authored assets are the right knee.
- Every id uses `knee_r.*` and `_R` / `_L` = the patient's side (contract §4).
- For a left knee, the Blender pipeline would need the `.l` objects and the mirrored pose. This is not provided.

## Clinical review requirements
1. **Anatomical accuracy and plausibility**, by an orthopaedic surgeon or anatomist:
   - attachment sites (cruciates, collaterals, menisci);
   - meniscal shape;
   - cartilage extent;
   - the patella position at 97.5° flexion.
2. **Positioning:** whether 97.5° knee flexion and 48° hip flexion with the foot flat suits the training workflow.
3. **Every placeholder state:** resection planes, guide-pin placement, exposure window size, patella retraction and closure appearance. Replace them with reviewed values in the scenario.
4. **Implant shapes:** whether generic, non-proprietary shapes are acceptable for teaching.
