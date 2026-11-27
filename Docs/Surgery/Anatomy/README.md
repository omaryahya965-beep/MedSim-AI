# Surgery: Patient, Knee Anatomy, Implants and Instruments (Person 2)

> **DRAFT — NOT CLINICALLY VALIDATED.** These are educational assets for a virtual Total Knee Arthroplasty (TKA) training simulation. They are not a clinical, patient-specific or medical-device model. A qualified orthopaedic surgeon must review them before any training use.

Branch: `feature/surgery-anatomy-assets`. Integration target: `feature/surgery-complete`. Contract: [`Docs/Surgery/SURGERY_CONTRACT.md`](../SURGERY_CONTRACT.md) v1.0.0.

## What this delivers

| Asset | Prefab | Notes |
|---|---|---|
| Surgical patient | `Assets/MedSimAI/Surgery/Anatomy/Prefabs/SurgicalPatient.prefab` | Independent of the ER patient. Supine, right knee flexed 97.5°, hip 48°, foot flat on the table. Contains `ANCHOR_SurgicalSite_Knee_R`, the knee prefab and the drapes. |
| Modular right knee | `Assets/MedSimAI/Surgery/Anatomy/Prefabs/KneeAnatomy_R.prefab` | 44 independently accessible structures, 7 interaction targets (`knee_r.*`) with `IAnatomyStateView`, visual states, placement and guide anchors. |
| Drapes | `Assets/MedSimAI/Surgery/Anatomy/Prefabs/SurgicalDrape.prefab` | Upper and lower sheets, anaesthesia screen drape and frame, thigh cuff, stockinette. Static meshes, no runtime cloth. |
| Implant set | `Assets/MedSimAI/Surgery/Anatomy/Prefabs/KneeImplantSet.prefab` | Display set of the five generic components. |
| Implants | `Assets/MedSimAI/Surgery/Instruments/Prefabs/Implants/<implantId>.prefab` | `implant.femoral_component`, `implant.femoral_trial`, `implant.tibial_tray`, `implant.tibial_trial`, `implant.poly_insert`. |
| Instruments | `Assets/MedSimAI/Surgery/Instruments/Prefabs/<instrumentId>.prefab` | 11 contract instruments plus 2 proposed extras (`inst.forceps`, `inst.suction`). |
| Instrument tray | `Assets/MedSimAI/Surgery/Instruments/Prefabs/InstrumentTray.prefab` and `InstrumentTray_Populated.prefab` | One `ANCHOR_InstrumentSlot_<id>` per instrument. |
| Test scene | `Assets/MedSimAI/Surgery/Scenes/Anatomy_Test.unity` | Temporary table, back table, implant table, display knee and the asset inspector. |
| Catalogs | `Instruments/Catalog/surgery_asset_catalog.json`, `Anatomy/Prefabs/anatomy_catalog.json` | Machine-readable ids, paths, anchors, sizes and colliders. |

**Contract note on scene naming:** the scene is named `Anatomy_Test.unity`, as in the contract. The brief called it `AnatomyAssets_Test.unity`.

## Open the test scene
1. Open `Assets/MedSimAI/Surgery/Scenes/Anatomy_Test.unity` and press **Play**. The project's display-mode bootstrap still runs; use Desktop mode.
2. The left panel (`AnatomyAssetInspector`) offers:
   - **Neutral test states:** Intact, Marked, Incised, Exposed, Guides placed, Resected, Trial fitted, Implanted, Closed.
   - **Per-target state buttons:** these call the same `IAnatomyStateView.SetState` that mechanics will use.
   - **Visibility toggles** by anatomical category.
   - **Show anchors:** grip in green, tip in red, `ANCHOR_*` in cyan, others in yellow, each with RGB axes.
   - **Exploded view** slider for the display knee.
   - **Camera presets.**
3. Mouse: right-drag orbits, middle-drag pans, the wheel zooms, left-click identifies a structure and shows its id, category and source.

## Rebuild or inspect the models
- **Unity:** *MedSim AI ▸ Surgery ▸ Build anatomy, implant and instrument assets* regenerates materials, every prefab, the catalogs and the test scene from `Models/*.fbx`. *MedSim AI ▸ Surgery ▸ Validate anatomy & instrument assets* runs the pre-integration checks.
- **Blender (4.2 LTS, portable):** run the pipeline scripts in [`pipeline/`](pipeline/) in this order: `build_patient.py`, `build_knee.py`, `build_knee2.py`, `build_drapes.py`, `build_instruments.py`. See [ANATOMY_REFERENCE.md](ANATOMY_REFERENCE.md). The scripts contain the author's absolute paths; adjust them.
- **Evidence and measurements:** [`tools/AnatomyEvidenceCapture.cs`](tools/AnatomyEvidenceCapture.cs) and [`tools/AnatomyPerfMeasure.cs`](tools/AnatomyPerfMeasure.cs). Run them with `unity command script eval` in Play mode. Images are in `Screenshots/Surgery/Anatomy/`.

## Import the production prefabs
- **Person 1:** parent `SurgicalPatient.prefab` to `ANCHOR_PatientRoot` with a zero local pose, then check the table fit as described in [INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md).
- **Person 3:** reference targets and anchors by their contract ids, not mesh names. See [PREFAB_CONTRACT.md](PREFAB_CONTRACT.md).
- **Person 4:** place the tray at `ANCHOR_InstrumentTable` and the implant set at `ANCHOR_ImplantTable`. See [INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md).

## Documents
[ANATOMY_REFERENCE](ANATOMY_REFERENCE.md) · [ASSET_LICENSES](ASSET_LICENSES.md) · [INSTRUMENT_CATALOG](INSTRUMENT_CATALOG.md) · [IMPLANT_CATALOG](IMPLANT_CATALOG.md) · [PREFAB_CONTRACT](PREFAB_CONTRACT.md) · [INTEGRATION_GUIDE](INTEGRATION_GUIDE.md) · [TEST_REPORT](TEST_REPORT.md) · [KNOWN_LIMITATIONS](KNOWN_LIMITATIONS.md)
