# Asset Credits

## Characters (Phase 2): MakeHuman / MPFB2
Generated with **MPFB 2.0.18** (MakeHuman Community; github.com/makehumancommunity/mpfb2) in portable **Blender 4.2.23 LTS**, both installed on D:. No account was used. The generator script is `Assets/MedSimAI/Characters/make_character_mpfb.py.txt`.

| Asset | Pack | License | Used for | Modifications |
|---|---|---|---|---|
| Base mesh, skins (young_caucasian_female, middleage_caucasian_male), eyes, eyebrows, eyelashes, teeth, tongue, short02 hair, female_casualsuit01, shoes02, game_engine rig | makehuman_system_assets | **CC0** | Both characters | Skin tint; grey hair texture derived from short02 |
| rehmanpolanski_hair_bun_brown | hair01 | CC0 | Dr. Hassan | — |
| toigo_shift_dress | dress01 | CC0 | Patient gown | Retextured as a printed hospital gown (derived from the original luminance) |
| **punkduck_winter_coat** | dress03 | **CC-BY** (author: *punkduck*, MakeHuman Community) | Dr. Hassan's lab coat | Retextured white; inflated 5 mm to avoid clipping. **Attribution is required.** |
| toigo_wool_pants | pants01 | CC0 | Downloaded but not used | — |
| visemes01 (Microsoft visemes), faceunits01 (ARKit face units) | functional packs from files.makehumancommunity.org | **No licence file inside the zip.** MakeHuman states its core assets are CC0. **Verify before redistribution.** | Lip sync, blink, expressions | — |

MakeHuman FAQ: "All core assets … are shared under CC0"; models may be used in closed-source products and sold. Third-party assets keep their own licences (here, CC-BY for the coat).

## Fonts
- Noto Sans Arabic Regular: SIL Open Font License 1.1 (© 2022 The Noto Project Authors), `Assets/MedSimAI/Art/Fonts/OFL.txt`.
- LiberationSans (TextMesh Pro essentials): OFL.

## Environment (Phase 1, CC0, Poly Haven)
- **Textures:** terrazzo_tiles, white_plaster_02, ceiling_interior, metal_plate_02, leather_white, terry_cloth, long_white_tiles, laminate_floor_02.
- **Models:** clipboard, drawer_cabinet, fire_alarm, medical_tape, metal_stool_02, metal_trash_can, modern_arm_chair_01, mounted_fluorescent_lights, office_notepads, metal_office_desk.
- Source: https://polyhaven.com/license. Individual authors are listed on each asset page.

## Packages
- XR Interaction Toolkit 3.7.0 Starter Assets (Unity Companion License).
- HTC VIVE OpenXR Plugin 2.5.1 (HTC VIVE SDK License Agreement; review it before commercial distribution).

## Generated in-project
- Room architecture, bed, trolley, monitor, ECG cart, instruments, curtains, chart and badge.
- Icon and splash.
- Synthesised clinical sounds (illustrative).
- Offline English voices: Windows SAPI *Microsoft David Desktop* (patient) and *Microsoft Zira Desktop* (supervisor). Check Microsoft's terms before commercial redistribution.

## Not used
Mixamo, Asset Store or Sketchfab content (all require an account), and untrusted scripts. I found no account-free, permissively licensed, high-detail medical equipment models (see KNOWN_LIMITATIONS).

---

## Environment redesign v0.2 (corridor, door, room upgrade)

All files below were obtained account-free (see `AssetStaging/MANIFEST.md`) and converted with portable Blender 4.2.23 by `scratch_env/convert.py` / `blanket.py` (import only; no downloaded script was executed). Converted meshes, textures and material descriptions live in `Assets/MedSimAI/ThirdParty/<Source>/<Asset>/`. This section supersedes the "Not used: Sketchfab content" note above.

### Sketchfab models, CC BY 4.0 (attribution required; obtained through the Objaverse 1.0 mirror)

| Title | Author | Source | Used for | Modifications |
|---|---|---|---|---|
| "Simple Hospital Bed" | Yvo Pors | https://sketchfab.com/3d-models/simple-hospital-bed-60651eac7e96454c8cf5c43a91412336 | Patient bed frame, rails, boards, castors (`HospitalBed`); pillow (`BedPillow`) | Scaled to a 2.0 m mattress; model mattress and coiled pendant cord removed; deck raised 0.10 m (hi-lo) to meet the in-project mattress; near-side rails lowered; pillow split out and resized; decimated 124k → 42k + 3k tris; cube-projected UVs; new materials |
| "HospitalDoor" | azizcharfeddine1997 | https://sketchfab.com/3d-models/hospitaldoor-5455e5d934d940729304d49c7afff73b | Exam Room 3 hinged door (frame, leaf, lever) and the three closed corridor end-door leaves | Mirrored so it opens into the room; leaf scaled to 1.04 × 2.10 m; lever handles split into a separate mesh with its pivot on the spindle; hinge pivot moved to the knuckle line |
| "Chair Hospital" | Khafesus | https://sketchfab.com/3d-models/chair-hospital-baaa4ec5d6d34ad49004e4d151b43aa2 | Corridor beam seating | Stretched 1.2× along the beam (0.44 m seat pitch); decimated 135k → 16k tris; new materials |
| "Covid-19 Sanitizer Station" | Marco669966 | https://sketchfab.com/3d-models/covid-19-sanitizer-station-9b8dcfdf83404606bbdf070e6a8497b0 | Corridor hand-sanitiser stand | Scaled to 1.55 m |
| "Pediatric Code Cart" | Mikpultro | https://sketchfab.com/3d-models/pediatric-code-cart-e55eb8ea1d6c47f1a4880e5532983850 | Resuscitation trolley in the exam room | Decimated 33k → 14k tris |
| "Adjustable Bedside Table" | Ethan Cragun | https://sketchfab.com/3d-models/adjustable-bedside-table-e3ef6fd54af440efa31984214dd6f11d | Over-bed table | Scaled to 0.98 m height |
| "IV Pole" | Mouch | https://sketchfab.com/3d-models/iv-pole-76521e2f71704b1986cf5ac54368b14f | IV stand with bag | One of the two poles kept; scaled to 2.0 m |
| "Rigged Stethoscope" | Simon Pasi | https://sketchfab.com/3d-models/rigged-stethoscope-e0d5ec7eb4c842599205c6f65b14bdd3 | Stethoscope visual | Armature pose baked to a static mesh; scaled 1.2× |
| "Sphygmomanometer Low Poly" | TheGiwi | https://sketchfab.com/3d-models/sphygmomanometer-low-poly-58f303b71fd047b0960785141485f8f0 | Blood-pressure cuff visual (loose state) | Scaled to 0.36 m |
| "Pulse Oximeter \| Free Model" | Nofil.Khan | https://sketchfab.com/3d-models/pulse-oximeter-free-model-e3ad5ac95aed4546bb869fcd984a7a99 | Pulse-oximeter visual | Scaled to 62 mm; decimated to 5k tris; its printed read-out is covered by the live display |

License text: https://creativecommons.org/licenses/by/4.0/ . The original credit lines are in `AssetStaging/HospitalModels/ATTRIBUTION_ALL.txt`.

### Poly Haven, CC0
| Asset | Author | Source | Used for | Modifications |
|---|---|---|---|---|
| SchoolChair_01 | Ethan Place | https://polyhaven.com/a/SchoolChair_01 | Visitor chair | Scaled to 0.86 m |
| korean_fire_extinguisher_01 | UM JOORIN | https://polyhaven.com/a/korean_fire_extinguisher_01 | Corridor fire extinguisher | Floor stand (with Korean text) cut away; wall-mounted with the label turned to the wall |
| urban_courtyard_02 (4K HDRI) | Sergej Majboroda | https://polyhaven.com/a/urban_courtyard_02 | Sky, window view and ambient light | Rotated; exposure 1.15 |

### ambientCG, CC0 (Lennart Demes, https://ambientcg.com)
PaintedPlaster017 (wall paint normal), OfficeCeiling001 (ceiling tiles), Metal009 (stainless / aluminium normal), Plastic013A (ABS, dado, powder coat normal), Fabric030 (linen, blanket, blind, notice-board felt normal), Leather026 (vinyl upholstery normal), Wood049 (handrail). 2K JPG, imported at 1024 px.

### Audio
| File | Source | Author | License | Used for |
|---|---|---|---|---|
| metalLatch.ogg, creak2.ogg, doorClose_4.ogg (+ doorOpen_1.ogg, spare) | Kenney "RPG Audio", https://kenney.nl/assets/rpg-audio | Kenney | CC0 | Door latch, swing and close |
| BSB_1471_air-conditioner.wav | https://bigsoundbank.com/air-conditioner-s1471.html | Joseph Sardin | CC0 | Corridor HVAC room tone (looped, volume 0.10) |
| BSB_0174_ventilation-2.wav | https://bigsoundbank.com/ventilation-2-s0174.html | Joseph Sardin | CC0 | Muffled plant noise behind the fire doors (low-pass 900 Hz, volume 0.06) |

### Fonts
- Noto Kufi Arabic Medium v2.110: SIL Open Font License 1.1 (© The Noto Project Authors), https://github.com/notofonts/arabic . Used for all Arabic signage (`Assets/MedSimAI/Art/Fonts/NotoKufiArabic SDF.asset`, generated by the builder).

### Made in-project for v0.2
Corridor shell and fixtures, signage, radiator, window, doctor stool, pedal bins, ECG cart frame and lead set, monitor housing details, towel dispenser, sheet-vinyl fleck texture (procedural), and the blanket (Blender cloth simulation over the reclined patient; `Assets/MedSimAI/ThirdParty/MedSim/Blanket`).

## Characters v0.2: Microsoft Rocketbox (replaces the MakeHuman characters in the scene)
Assets are in `Assets/MedSimAI/Characters/v2/`. Preparation notes and Blender scripts: `AssetStaging/CharacterPrep/README.md`, `D:\Medical-Test\scratch_chars\`.

| Asset | Source | License | Used for | Modifications |
|---|---|---|---|---|
| Avatar `Medical_Female_02` (f153) | Microsoft Rocketbox Avatar Library, https://github.com/microsoft/Microsoft-Rocketbox | **MIT**, Copyright (c) 2020 Microsoft | Dr. Sara Hassan | Shirt, trousers, hair and iris recoloured; ID badge card added; clavicles re-parented to Spine2; 74 alias blend shapes added (ARKit and Microsoft viseme names) |
| Avatar `Male_Adult_05` (m009) | same | MIT | Mr. Reyes: head, face rig, skeleton | Iris recoloured, skin desaturated 12% |
| Avatar `Male_Adult_09` (m017) | same (downloaded from the repo; not in `AssetStaging/MANIFEST.md`) | MIT | Mr. Reyes: body and bare arms | Grafted onto the `Male_Adult_05` skeleton; skin tone matched; forearm tattoo painted out; T-shirt repainted as skin under the gown |
| 18 animation clips (`f_idle_*`, `f_gestic_*`, `f_walk_*`, `f_invite_sit`, `f_documents_idle`) | Rocketbox `Assets/Animations` | MIT | Dr. Hassan's Animator | Re-baked onto the avatar skeleton at 30 fps |
| uLipSync 3.1.5 (`com.hecomi.ulipsync`, UPM git `#upm@3.1.5`), incl. its sample MFCC profiles | hecomi, https://github.com/hecomi/uLipSync | **MIT** | Audio-driven lip sync | None. The package's Unity-chan samples are not imported |

The MIT licence requires the copyright notice and licence text to ship with the build: include `AssetStaging/Rocketbox/LICENSE.md` (Microsoft) and the uLipSync `LICENSE.md` in the credits screen or a licence file.

Research citation if results are published: Gonzalez-Franco et al. 2020, "The Rocketbox Library and the Utility of Freely Available Rigged Avatars", *Frontiers in Virtual Reality*, doi:10.3389/frvir.2020.561558.

**Made for this project in Blender:** hospital gown, wristband, blanket, badge texture, the reclined pose and all 11 patient clips (`Patient_Reclined_*`, `PainShift`, `HandToChest`, `Wince`, `Nod`, `ShakeNo`, `Think`).

**Staged but not used:** Quaternius Universal Animation Library 1 and 2 (CC0 1.0); Rocketbox `Male_Adult_01`, `_11`, `_16` (MIT; arm-donor candidates in `scratch_chars\dl\`).

**No longer used in the scene** (assets remain in the project): the MakeHuman / MPFB2 characters above, including the CC-BY `punkduck_winter_coat`. If those assets are deleted, the CC-BY attribution can be dropped.
