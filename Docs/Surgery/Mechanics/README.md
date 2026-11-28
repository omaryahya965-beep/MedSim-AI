# MedSim AI — Surgical Gameplay, Interaction, Physics and Simulation Engine (Person 3)

> **DRAFT — NOT CLINICALLY VALIDATED**  
> *«مسودة — غير معتمدة سريرياً»*  
> The surgical module is an educational simulation for a student project. It is not a substitute for clinical supervision or accredited surgical training, and its scores are not a competency certification. A qualified orthopaedic surgeon must review the clinical content before any real training use.

---

## 1. Overview

This module (`MedSimAI.Surgery.Mechanics`) implements the surgical gameplay, tool manipulation, anatomical state transitions, implant positioning validation, robotic guidance, and session reset systems for the **Total Knee Replacement (TKA) Surgery** educational simulation in MedSim AI.

It strictly adheres to [`SURGERY_CONTRACT.md`](../SURGERY_CONTRACT.md) v1.1.0, decoupling simulation mechanics from the AI supervisor, environment, and 3D asset production.

---

## 2. Key Features

- **Authored Scenario Execution:** Fully data-driven scenario engine (`SurgeryScenarioEngine`) parsing `OR_TKA_TRAINING_001.json`, enforcing objective prerequisites, expected tools, target regions, and tolerances.
- **Instrument Interaction System:** Supports grabbing, releasing, orientation tracking, power tool activation (oscillating saw, drill), and registry state tracking (`SurgicalInstrumentController` implementing `IInstrumentRegistry`).
- **Anatomical State Transitions:** Deterministic state machine (`AnatomyStateController` & `AnatomyInteractionController` implementing `IAnatomyStateView`) supporting states: `intact`, `marked`, `incised`, `exposed`, `guide_placed`, `resected`, `trial_fitted`, `implanted`, and `closed`.
- **Virtual Implant Placement:** Interactive positioning and orientation validation with configurable clinical tolerances (`toleranceMm`, `toleranceDeg`), snap seating, and rejection of misplaced components (`ImplantPlacementController`).
- **Robotic Surgical Guidance:** Controlled educational robotic assistance (`SurgicalRobotController`) providing workspace constraints and guidance states.
- **Dual Mode Support:** Unified simulation logic supporting both **VIVE Focus 3 VR Mode** (via OpenXR and XR Interaction Toolkit 3.7.0) and **Desktop Mode** (`SurgicalDesktopInteractionAdapter` with mouse manipulation, depth wheel, and keyboard controls).
- **Session Reset & Cleanup:** Full session reset restoring pristine anatomy, tools to Mayo table slots, implants to table, and objective sequence without requiring scene reload (`SurgeryResetController`).
- **Event Bus Integration:** Publishes verified gameplay events (`objective.*`, `tool.*`, `anatomy.*`, `implant.*`, `interaction.rejected`) with `source = "mechanics"` to the session's `SurgeryEventBus`.
- **AI Independence:** Operates 100% deterministically without requiring an internet connection or the OpenAI backend service.

---

## 3. Owned Folders & Scenes

Per contract §18:
* `Assets/MedSimAI/Surgery/Mechanics/` — All simulation scripts, controllers, and prefabs.
* `Assets/MedSimAI/Surgery/Scenes/SurgeryMechanics_Test.unity` & `Mechanics_Test.unity` — Isolated test scenes.
* `Assets/MedSimAI/Surgery/Tests/Mechanics/` — EditMode and PlayMode unit tests.
* `Docs/Surgery/Mechanics/` — Module architectural and user documentation.

---

## 4. Quick Start

### In Unity Editor
1. Open scene: `Assets/MedSimAI/Surgery/Scenes/SurgeryMechanics_Test.unity`.
2. Press **Play**.
3. In Desktop mode:
   * **WASD** to walk, mouse to look.
   * **Left-Click** to pick up instrument / implant.
   * **Scroll Wheel** to push / pull tool depth.
   * **Right-Click Drag** to rotate instrument.
   * **E** to activate power tool (e.g. saw).
   * **Left-Click** on knee to perform surgical action.
   * **Q** to drop / release.
   * **R** to reset session.
