# Prefab and Identifier Contract (Person 2)

This complements [`SURGERY_CONTRACT.md`](../SURGERY_CONTRACT.md) v1.0.0. Where the two differ, the shared contract wins; report mismatches to Person 4. Every id below is stable. Mechanics should look objects up by these ids or anchor names, never by mesh names.

## Shared components and the serialization workaround
- **The problem:** the contract tags (`SurgeryInstrumentTag`, `SurgeryAnatomyTag`, `SurgeryImplantTag`) live in `Shared/Unity/SurgerySceneTags.cs` together with `SurgeryAnchor` and `SurgeryEventBusHost`. Unity serializes only the class a script file maps to, here `SurgeryAnchor`. Saved in a prefab or scene, the other four become missing scripts.
- **The workaround:** my prefabs carry serializable bridges that add the real tag in `Awake`, at execution order −1000, before any mechanics or AI code:
  - `InstrumentTagBridge` → `SurgeryInstrumentTag` (instrumentId, tip, displayNameEn and displayNameAr);
  - `ImplantTagBridge` → `SurgeryImplantTag` (implantId; placementTarget optional);
  - `AnatomyTargetStateView` → `SurgeryAnatomyTag` (targetId). It also implements `IAnatomyStateView`.
- **At runtime** the contract components exist exactly as specified (PlayMode tests verify this). In the Editor, before Play, use the bridge fields.
- **Requested fix (Person 4):** move each of the four classes to its own file (`SurgeryInstrumentTag.cs`, `SurgeryAnatomyTag.cs`, `SurgeryImplantTag.cs`, `SurgeryEventBusHost.cs`) and keep `SurgeryAnchor` in `SurgerySceneTags.cs`, so existing anchors in Person 1's prefabs keep their references. The bridges can then be replaced by the real tags.

## `SurgicalPatient.prefab`
```
SurgicalPatient                      SurgicalPatientInfo (operativeSide "R", flexion angles, width)
├── Body                             Body, RightLeg_Prepped, Brief, Gown, Hair, Eyes, Eyebrows, Eyelashes, Teeth, Tongue (AnatomyStructure)
├── JointAnchors                     Joint_Hip_R/L, Joint_Knee_R/L, Joint_Ankle_R/L, Joint_Pelvis, Joint_Neck, Joint_Head, Joint_HeadTop, Joint_Shoulder_R/L
├── ANCHOR_SurgicalSite_Knee_R       SurgeryAnchor — right knee joint centre (−0.154, 0.398, −0.235)
│   └── KneeAnatomy_R (nested prefab)
└── SurgicalDrape (nested prefab)
```

## `KneeAnatomy_R.prefab`
```
KneeAnatomy_R                        KneeAnatomyStateController
├── Bones          FemurShaft, DistalFemur, DistalFemur_Resected*, TibiaShaft, ProximalTibia, ProximalTibia_Resected*, Fibula,
│                  PatellaGroup (pivot = tibial tuberosity) → Patella, PatellarCartilage, PatellarTendon, PatellaRoot, knee_r.patella target;
│                  PatellaRetractedPose
├── Cartilage      FemoralCartilage, TibialCartilage
├── Menisci        MedialMeniscus, LateralMeniscus, TransverseLigament, MeniscotibialLigaments
├── Ligaments      ACL, PCL, MCL, LCL, PoplitealLigaments, PatellarRetinacula
├── Tendons        Quadriceps
├── SoftTissue     JointCapsule, InfrapatellarFatPad, SuprapatellarBursa, OptionalMuscles/* (inactive)
├── Skin           KneeSkin, KneeSkin_Open*, SiteMarking*, IncisionLine*, ClosureLine*, SkinStaples*
├── StateVisuals   GuidePins_Femur*, GuidePins_Tibia*, ComponentVisuals/{FemoralTrial, FemoralComponent, TibialTrial, TibialTray, PolyInsert}*
├── InteractionTargets  knee_r.site_marking, knee_r.incision_line, knee_r.joint_capsule, knee_r.distal_femur, knee_r.proximal_tibia, knee_r.wound
├── ImplantAnchors      PlacementTarget_<implantId> ×5, GuideTarget_inst.cutting_guide_femoral, GuideTarget_inst.cutting_guide_tibial
└── InteractionAnchors  KneeRoot, FemurRoot, TibiaRoot, FibulaRoot
(* inactive by default — switched by the state controller)
```
- **Colliders:**
  - Each target has a trigger `BoxCollider`.
  - Bones, cartilage and menisci also have non-convex static `MeshCollider`s, available for contact queries.
  - All mesh assets are imported Read/Write.
- **Guide targets:** `GuideTarget_*` has +Z pointing into the bone and +Y proximal. Align the guide prefab's `SeatPoint` with it.

### Interaction targets and anchors
| Target id | Path | Supported states | Collider |
|---|---|---|---|
| `knee_r.patella` | `Bones/PatellaGroup/knee_r.patella` | `intact` → `exposed` | BoxCollider (trigger) [0.05,0.05,0.03] |
| `knee_r.site_marking` | `InteractionTargets/knee_r.site_marking` | `intact` → `marked` | BoxCollider (trigger) [0.03,0.05057,0.16105] |
| `knee_r.incision_line` | `InteractionTargets/knee_r.incision_line` | `intact` → `incised` | BoxCollider (trigger) [0.02,0.05057,0.16105] |
| `knee_r.wound` | `InteractionTargets/knee_r.wound` | `intact` → `closed` | BoxCollider (trigger) [0.075,0.06057,0.16105] |
| `knee_r.joint_capsule` | `InteractionTargets/knee_r.joint_capsule` | `intact` → `exposed` | BoxCollider (trigger) [0.07,0.06,0.05] |
| `knee_r.distal_femur` | `InteractionTargets/knee_r.distal_femur` | `intact` → `guide_placed` → `resected` → `trial_fitted` → `implanted` | BoxCollider (trigger) [0.075,0.05,0.065] |
| `knee_r.proximal_tibia` | `InteractionTargets/knee_r.proximal_tibia` | `intact` → `guide_placed` → `resected` → `trial_fitted` → `implanted` | BoxCollider (trigger) [0.075,0.03,0.055] |

| Anchor | Path | Local position (m) | Local rotation (quat) |
|---|---|---|---|
| `PatellaRoot` | `Bones/PatellaGroup/PatellaRoot` | (-0.009, 0.006, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `PatellaRetractedPose` | `Bones/PatellaRetractedPose` | (-0.031, -0.036, -0.053) | (0.018, 0.243, 0.345, 0.906) |
| `PlacementTarget_implant.femoral_component` | `ImplantAnchors/PlacementTarget_implant.femoral_component` | (0.003, -0.009, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `PlacementTarget_implant.femoral_trial` | `ImplantAnchors/PlacementTarget_implant.femoral_trial` | (0.003, -0.009, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `PlacementTarget_implant.tibial_tray` | `ImplantAnchors/PlacementTarget_implant.tibial_tray` | (0.009, -0.037, -0.011) | (0.004, 0.922, 0.381, -0.063) |
| `PlacementTarget_implant.tibial_trial` | `ImplantAnchors/PlacementTarget_implant.tibial_trial` | (0.009, -0.037, -0.011) | (0.004, 0.922, 0.381, -0.063) |
| `PlacementTarget_implant.poly_insert` | `ImplantAnchors/PlacementTarget_implant.poly_insert` | (0.009, -0.034, -0.008) | (0.004, 0.922, 0.381, -0.063) |
| `GuideTarget_inst.cutting_guide_femoral` | `ImplantAnchors/GuideTarget_inst.cutting_guide_femoral` | (0.003, 0.003, 0.019) | (0.946, 0.046, -0.016, 0.321) |
| `GuideTarget_inst.cutting_guide_tibial` | `ImplantAnchors/GuideTarget_inst.cutting_guide_tibial` | (-0.002, -0.02, -0.026) | (0.381, 0.063, -0.004, 0.922) |
| `KneeRoot` | `InteractionAnchors/KneeRoot` | (0, 0, 0) | (0, 0, 0, 1) |
| `FemurRoot` | `InteractionAnchors/FemurRoot` | (0.003, -0.009, 0.004) | (0.016, 0.321, 0.946, -0.046) |
| `TibiaRoot` | `InteractionAnchors/TibiaRoot` | (0.009, -0.037, -0.011) | (0.004, 0.922, 0.381, -0.063) |
| `FibulaRoot` | `InteractionAnchors/FibulaRoot` | (-0.029, -0.049, 0.001) | (0.004, 0.922, 0.381, -0.063) |

### Structures (`AnatomyStructure.structureId`)
| Structure id | Path in KneeAnatomy_R | Category | Name (EN / AR) | Active by default | Placeholder |
|---|---|---|---|---|---|
| `knee_r.bone.patella` | `Bones/PatellaGroup/Patella` | Bone | Patella / الرضفة | yes |  |
| `knee_r.cartilage.patellar` | `Bones/PatellaGroup/PatellarCartilage` | Cartilage | Patellar articular cartilage / غضروف الرضفة | yes |  |
| `knee_r.tendon.patellar` | `Bones/PatellaGroup/PatellarTendon` | Tendon | Patellar tendon (ligament) — procedural approximation / وتر الرضفة (تقريب إجرائي) | yes |  |
| `knee_r.bone.femur_shaft` | `Bones/FemurShaft` | Bone | Femur (shaft) / جسم عظم الفخذ | yes |  |
| `knee_r.bone.distal_femur` | `Bones/DistalFemur` | Bone | Distal femur / النهاية السفلية لعظم الفخذ | yes |  |
| `knee_r.bone.distal_femur_resected` | `Bones/DistalFemur_Resected` | Bone | Distal femur — illustrative resection / الفخذ بعد القطع (توضيحي) | no | yes |
| `knee_r.bone.tibia_shaft` | `Bones/TibiaShaft` | Bone | Tibia (shaft) / جسم عظم الساق | yes |  |
| `knee_r.bone.proximal_tibia` | `Bones/ProximalTibia` | Bone | Proximal tibia / النهاية العلوية لعظم الساق | yes |  |
| `knee_r.bone.proximal_tibia_resected` | `Bones/ProximalTibia_Resected` | Bone | Proximal tibia — illustrative resection / الساق بعد القطع (توضيحي) | no | yes |
| `knee_r.bone.fibula` | `Bones/Fibula` | Bone | Fibula / عظم الشظية | yes |  |
| `knee_r.cartilage.femoral` | `Cartilage/FemoralCartilage` | Cartilage | Femoral articular cartilage / الغضروف المفصلي لعظم الفخذ | yes |  |
| `knee_r.cartilage.tibial` | `Cartilage/TibialCartilage` | Cartilage | Tibial articular cartilage / الغضروف المفصلي لعظم الساق | yes |  |
| `knee_r.meniscus.medial` | `Menisci/MedialMeniscus` | Meniscus | Medial meniscus / الغضروف الهلالي الإنسي | yes |  |
| `knee_r.meniscus.lateral` | `Menisci/LateralMeniscus` | Meniscus | Lateral meniscus / الغضروف الهلالي الوحشي | yes |  |
| `knee_r.ligament.transverse` | `Menisci/TransverseLigament` | Ligament | Transverse ligament of the knee / الرباط المستعرض للركبة | yes |  |
| `knee_r.ligament.meniscotibial` | `Menisci/MeniscotibialLigaments` | Ligament | Meniscotibial (coronary) ligaments / الأربطة الهلالية الظنبوبية | yes |  |
| `knee_r.ligament.acl` | `Ligaments/ACL` | Ligament | Anterior cruciate ligament / الرباط الصليبي الأمامي | yes |  |
| `knee_r.ligament.pcl` | `Ligaments/PCL` | Ligament | Posterior cruciate ligament / الرباط الصليبي الخلفي | yes |  |
| `knee_r.ligament.mcl` | `Ligaments/MCL` | Ligament | Medial (tibial) collateral ligament / الرباط الجانبي الإنسي | yes |  |
| `knee_r.ligament.lcl` | `Ligaments/LCL` | Ligament | Lateral (fibular) collateral ligament / الرباط الجانبي الوحشي | yes |  |
| `knee_r.ligament.popliteal` | `Ligaments/PoplitealLigaments` | Ligament | Oblique & arcuate popliteal, popliteofibular ligaments / الأربطة المأبضية | yes |  |
| `knee_r.ligament.patellar_retinacula` | `Ligaments/PatellarRetinacula` | Ligament | Patellar retinacula & meniscopatellar ligaments / قيد الرضفة والأربطة الهلالية الرضفية | yes |  |
| `knee_r.tendon.quadriceps` | `Tendons/Quadriceps` | Tendon | Quadriceps tendon and distal quadriceps / وتر العضلة رباعية الرؤوس | yes |  |
| `knee_r.soft.joint_capsule` | `SoftTissue/JointCapsule` | SoftTissue | Articular capsule of the knee / محفظة مفصل الركبة | yes |  |
| `knee_r.soft.infrapatellar_fat_pad` | `SoftTissue/InfrapatellarFatPad` | SoftTissue | Infrapatellar fat pad / الوسادة الدهنية تحت الرضفة | yes |  |
| `knee_r.soft.suprapatellar_bursa` | `SoftTissue/SuprapatellarBursa` | SoftTissue | Suprapatellar bursa / الجراب فوق الرضفة | yes |  |
| `knee_r.muscle.gastrocnemius` | `SoftTissue/OptionalMuscles/Gastrocnemius` | Muscle | Gastrocnemius heads (sectioned) / رأسا العضلة التوأمية | no |  |
| `knee_r.muscle.popliteus` | `SoftTissue/OptionalMuscles/Popliteus` | Muscle | Popliteus / العضلة المأبضية | no |  |
| `knee_r.muscle.pes_anserinus` | `SoftTissue/OptionalMuscles/PesAnserinus` | Muscle | Sartorius, gracilis, semitendinosus (sectioned) / عضلات القدم الإوزية | no |  |
| `knee_r.muscle.hamstrings` | `SoftTissue/OptionalMuscles/Hamstrings` | Muscle | Biceps femoris, semimembranosus (sectioned) / عضلات أوتار المأبض | no |  |
| `knee_r.soft.iliotibial_tract` | `SoftTissue/OptionalMuscles/IliotibialTract` | Muscle | Iliotibial tract (sectioned) / السبيل الحرقفي الظنبوبي | no |  |
| `knee_r.skin.intact` | `Skin/KneeSkin` | Skin | Knee skin (prepped) / جلد الركبة (معقّم) | yes |  |
| `knee_r.skin.open` | `Skin/KneeSkin_Open` | Skin | Knee skin with retracted wound (placeholder) / جلد الركبة مع الجرح مفتوحاً (توضيحي) | no | yes |
| `knee_r.overlay.site_marking` | `Skin/SiteMarking` | SkinOverlay | Site marking line / خط تعليم الموقع | no | yes |
| `knee_r.overlay.incision` | `Skin/IncisionLine` | SkinOverlay | Incision line / خط الشق | no | yes |
| `knee_r.overlay.closure` | `Skin/ClosureLine` | SkinOverlay | Closed wound line / خط الجرح المغلق | no | yes |
| `knee_r.overlay.staples` | `Skin/SkinStaples` | SkinOverlay | Skin staples / دبابيس الجلد | no | yes |
| `knee_r.state.guide_pins_femur` | `StateVisuals/GuidePins_Femur` | StateVisual | Femoral guide pins (placeholder) / مسامير دليل الفخذ (توضيحي) | no | yes |
| `knee_r.state.guide_pins_tibia` | `StateVisuals/GuidePins_Tibia` | StateVisual | Tibial guide pins (placeholder) / مسامير دليل الساق (توضيحي) | no | yes |
| `knee_r.visual.femoral_trial` | `StateVisuals/ComponentVisuals/FemoralTrial` | ComponentVisual | Femoral trial (visual) / قطعة الفخذ التجريبية | no | yes |
| `knee_r.visual.femoral_component` | `StateVisuals/ComponentVisuals/FemoralComponent` | ComponentVisual | Femoral component (visual) / مكوّن الفخذ | no | yes |
| `knee_r.visual.tibial_trial` | `StateVisuals/ComponentVisuals/TibialTrial` | ComponentVisual | Tibial trial (visual) / قطعة الساق التجريبية | no | yes |
| `knee_r.visual.tibial_tray` | `StateVisuals/ComponentVisuals/TibialTray` | ComponentVisual | Tibial tray (visual) / صينية الساق | no | yes |
| `knee_r.visual.poly_insert` | `StateVisuals/ComponentVisuals/PolyInsert` | ComponentVisual | Polyethylene insert (visual) / الفاصل البلاستيكي | no | yes |

## State behaviour (`KneeAnatomyStateController`)
| Target state | Visual result |
|---|---|
| `site_marking=marked` | Purple marking line and ticks on the skin (hidden once incised) |
| `incision_line=incised` | Fresh incision line |
| `joint_capsule=exposed` | `KneeSkin_Open` replaces `KneeSkin` (window and wound walls). Capsule, fat pad, retinacula, bursa, quadriceps and patellar tendon are hidden. `PatellaGroup` blends (0.5 s) to `PatellaRetractedPose`. |
| `patella=exposed` | Patella retracted, even if the capsule is intact |
| `distal_femur=guide_placed` | Femoral guide pins |
| `distal_femur=resected / trial_fitted / implanted` | `DistalFemur_Resected` replaces `DistalFemur`; femoral cartilage hidden. Component visuals appear only if `showComponentVisuals` is on. |
| `proximal_tibia=guide_placed` | Tibial guide pins |
| `proximal_tibia=resected / trial_fitted / implanted` | `ProximalTibia_Resected` replaces `ProximalTibia`; tibial cartilage, menisci, transverse and meniscotibial ligaments and ACL hidden |
| `wound=closed` | Skin closed with closure line and staples; patella back home; soft tissue shown again |

- **Reset:** `ResetAll()` returns every target to `intact`.
- **Refusals:** unsupported states are refused and change nothing.
- **Enclosure culling (performance):** while the intact skin is shown, the renderers of the deep anatomy are forced off (`forceRenderingOff`). Colliders stay active. Disable it with `cullEnclosedAnatomy`.
