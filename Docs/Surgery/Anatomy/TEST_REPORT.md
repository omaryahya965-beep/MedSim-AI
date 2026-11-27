# Test Report: Person 2 assets (2026-11-28)

Unity 6000.3.29f1, URP 17.3.0, Windows 11, NVIDIA RTX 3050 Laptop (4 GB). Branch `feature/surgery-anatomy-assets` on `origin/feature/surgery-complete` (`c9d03fe`, which itself sits on v0.5 `c1b73d6`). No paid API calls were made.

## Automated results
| Suite | Result |
|---|---|
| Unity EditMode (whole project) | **127 / 127**: 98 existing ER tests + Person 4's shared-contract tests + **14 new** `SurgeryAnatomyAssetTests` |
| Unity PlayMode (whole project) | **28 / 28**: 24 existing ER tests + **4 new** `SurgeryAnatomyRuntimePlayTests` |
| Proxy (`node --test`) | **32 / 32**, unchanged (backend not touched; this branch's v0.5 baseline) |
| `SurgeryAssetValidator.ValidateAll()` | **0 issues** |
| Compile | 0 errors |

The baseline quoted in the brief (EditMode 98, PlayMode 29, proxy 34) is v0.6 on `master`. The integration branch is v0.5, whose ER baseline is EditMode 98, PlayMode 24 and proxy 32, all passing.

### New EditMode tests (`Assets/MedSimAI/Surgery/Tests/Anatomy/EditMode`)
- **Validator, required prefabs:** no issues; every required prefab exists.
- **Targets:** all 7 `AnatomyTargetIds` exist once each, with trigger colliders and `IAnatomyStateView`. Supported states are a superset of the scenario JSON's `targets[].states`.
- **Structures:** structure ids are unique, and the key structures are present.
- **State transitions:**
  - marked → incised → exposed → guide placed → resected → implanted → closed each swap the right visuals;
  - `ResetAll` restores intact;
  - an unsupported state is refused and changes nothing.
- **Patella:** in the exposed state the patella group reaches its retracted pose, and reset returns it.
- **Enclosure culling:** deep anatomy is not rendered under intact skin, colliders stay active, and it renders once the joint is exposed or the skin is hidden by a tool.
- **Tag bridges:** produce `SurgeryAnatomyTag`, `SurgeryInstrumentTag` and `SurgeryImplantTag` with the right ids.
- **Instrument conventions:**
  - unique ids, unit scale, grip at origin, tip toward +Z;
  - length 50–400 mm, with the scalpel ≈ 0.15 m as in the contract;
  - Rigidbody, colliders, and XR grab attached at the grip;
  - extras flagged.
- **Implants (key test):** each implant prefab placed at its `PlacementTarget_<id>` coincides with the authored component **within 1 mm**, for both the vertex centroid and the farthest vertex. This checks the whole Blender→Unity frame chain.
- **Patient:**
  - right knee at −X, head toward +Z, feet toward −Z;
  - the site anchor equals the right knee joint;
  - the knee is nested at the site;
  - width < 0.56 m.
- **Tray:** a slot for each instrument; no overlaps in the populated tray.
- **Materials:** every material is URP Lit.

### New PlayMode tests (`Assets/MedSimAI/Surgery/Tests/Anatomy/PlayMode`)
- **Patient `Awake`:** creates all 7 `SurgeryAnatomyTag`s, and `SurgeryAnchor.Find(ANCHOR_SurgicalSite_Knee_R)` resolves.
- **Instrument `Awake`:** creates `SurgeryInstrumentTag`; the saw blade `InstrumentMovablePart` poses and resets.
- **Patella retraction:** blends over time (not instantly), reaches its pose, and resets.
- **Implant:** the tag is created, and the placement target can be assigned.

## Asset measurements
| Asset | Value |
|---|---|
| Knee (all structures) | 184k triangles across all variants. About 170k render in the intact state when the skin is hidden; when the skin is shown, enclosure culling removes them |
| Patient prefab (body, knee, drapes; active renderers) | 250k triangles; 22 materials |
| Drapes | 35k triangles (height-field drape, decimated) |
| Instruments + tray | 47k triangles (13 instruments) |
| Implant set | 48k triangles (5 components) |
| Femoral component | 66.4 × 61.4 × 50.0 mm (ML × PD × AP), one connected shell, 0 non-manifold edges |
| Tibial tray / insert | tray 67.6 × 41.5 × 37.9 mm incl. keel and fins (4 mm plate); insert 55.6 × 10.9 × 36.5 mm |
| Incision path | 166–196 mm (depends on the skin pass) |
| Pose fit | Femur–tibia penetration 0.06 mm; patella clearance about 1 mm; shank axis error 0° after alignment |

## Rendering performance (Editor Play mode, game view 543 × 638, quality "PC"; **not headset performance**)
| View | Avg frame | p95 | p99 | Batches | Triangles rendered (incl. shadows) |
|---|---|---|---|---|---|
| Surgeon view, knee exposed | 4.19 ms (239 fps) | 5.31 ms | 12.47 ms | 227 | 1.21 M |
| Surgeon view, implanted | 3.49 ms (287 fps) | 4.20 ms | 4.86 ms | 199 | 0.94 M |
| Room overview (all assets) | 4.21 ms (238 fps) | 5.35 ms | 5.90 ms | 483 | 1.23 M |

- **Before optimisation**, the room overview rendered 2.61 M triangles. Drape decimation and enclosure culling halved that.
- **VRAM:** about 1.1 GB in use by the whole Editor.
- **Caveat:** the numbers come from a small Editor game view with the Editor's overhead. The real target is PCVR on the VIVE Focus 3 at two eyes × about 2448 × 2448. **Measure in the integrated OR scene on the headset**; this was not done.

## Visual evidence (`Screenshots/Surgery/Anatomy/`, 1600 × 900 JPEG, URP with post-processing)
| # | File | Shows |
|---|---|---|
| 1 | `01_surgical_patient_complete` | Patient without drapes (gown, brief, prepped right leg) |
| 2 | `02_patient_on_temporary_table` | Patient on the 0.51 × 2.09 m test table |
| 3 | `03_surgical_drape`, `03b_draped_knee_surgeon_view` | Draping: screen, sheets, thigh cuff, stockinette |
| 4 | `04_knee_anatomy_complete` | Whole knee anatomy |
| 5 | `05_knee_front` | Anterior view |
| 6 | `06_knee_lateral` | Lateral view |
| 7 | `07_knee_medial_posterior` | Posteromedial view |
| 8 | `08_individual_bones` | Bones only |
| 9 | `09_cartilage_and_menisci` | Cartilage and menisci |
| 10 | `10_ligaments_with_bones`, `10b_ligaments_tendons_menisci_only` | Ligaments |
| 11 | `11_exploded_anatomy` | Exploded view |
| 12 | `12_implant_components` | Five implant components |
| 13 | `13_instrument_collection` | Populated tray (13 instruments) |
| 14 | `14_instruments_closeup`, `14_closeup_*` (saw, needle driver, femoral guide, scalpel, retractor, stapler) | Instrument close-ups |
| 15 | `15_instrument_grip_and_tip_anchors` | Grip (green), tip (red) and other anchors with axes |
| 16 | `16_implant_placement_anchors`, `16b_components_seated_at_targets` | Placement anchors on the resected knee, and components seated |
| 17 | `17_00…17_08_state_*`, `17_knee_state_sequence.gif` | Intact → marked → incised → exposed → guides → resected → trial → implanted → closed |
| 18 | `18_asset_demonstration_scene` | Whole test scene |

## Not verified
- VIVE Focus 3 headset: rendering, scale perception, grabbing. **Not tested.**
- Integration in Person 1's `OperatingRoom.prefab` and with Person 3's mechanics; their branches are not merged.
- Clinical or anatomical review by a qualified specialist.
