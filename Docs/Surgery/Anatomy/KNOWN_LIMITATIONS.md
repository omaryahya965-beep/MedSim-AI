# Known Limitations: Person 2 assets

1. **Shared tag serialization (contract issue).**
   - `SurgeryInstrumentTag`, `SurgeryAnatomyTag`, `SurgeryImplantTag` and `SurgeryEventBusHost` share `Shared/Unity/SurgerySceneTags.cs` with `SurgeryAnchor`. Unity cannot save them in prefabs or scenes.
   - My prefabs add them at runtime through bridges (execution order −1000). Before Play, the tags are not on the objects; tools must read the bridge fields.
   - Person 4 needs to split the file. Changing `Shared/` was outside my remit.
2. **Room fit (checked in the Editor only).** The patient fits Person 1's table. Open items are in INTEGRATION_GUIDE, "Checked in your merged OperatingRoom.prefab":
   - the room's `SurgicalSite` transform is 0.39 m from the real knee site;
   - the tray overlaps the room's placeholder containers;
   - the left fingertips show at the drape edge;
   - the body compresses about 2 cm into the deck pad.
3. **Clinical status.** Everything is **DRAFT — NOT CLINICALLY VALIDATED**:
   - resection planes, cut thicknesses, guide-pin positions, exposure size, patella retraction and closure are illustrative placeholders;
   - the implants are generic shapes, not medical devices;
   - the anatomy is a healthy atlas knee, not an arthritic TKA candidate.
4. **Anatomy fidelity.**
   - Source meshes are BodyParts3D at 99% reduction, refined by subdivision. Close-up detail is smooth, not CT-sharp.
   - The patellar tendon is procedural.
   - Neurovascular structures are absent.
   - Muscles are sectioned and the deep posterior muscles are inactive.
   - Soft tissue was posed by distance-weighted blending, not simulated.
   - The flexion pose is fixed at 97.5°. There is no rig to change knee flexion at runtime.
5. **Skin.**
   - The flexed MPFB leg compresses the popliteal crease, and posterior structures would poke through. They ship inactive.
   - The knee skin was tightened over the anterior knee, which makes it look lean and bony.
   - A faint seam between the knee patch and the leg can be visible under grazing light.
   - In open states, a skin mound over the tibial tubercle can occlude part of the window from low viewpoints.
   - The everted or retracted patellar tendon is hidden in open states.
6. **Exposure.**
   - The opening is a fixed lens-shaped window with stylised wound walls (subcutaneous fat, then tissue). There are no retractor deformations, no bleeding, no capsule flap.
   - The view into the joint is narrow from the student's side. A larger window or a retractor pose may be needed.
7. **Patient.**
   - Static mesh with no skeleton at runtime (anaesthetised and positioned).
   - The body is MakeHuman quality with no subsurface scattering, and the hair is cards.
   - The gown and brief are derived from the body surface and have jagged edges, which are only visible with drapes off.
   - The arms rest across the chest under the drape; there are no arm boards.
   - Left and right are fixed, with the right knee operative.
8. **Drapes.**
   - Static height-field and cloth meshes, not interactive.
   - They assume a 0.51 × 2.09 m deck. A different table may show gaps or clipping at the edges.
   - The foot-end tent over the left foot is tall.
   - Seams where the two sheets meet are hidden by the thigh cuff.
9. **Instruments.**
   - Original simplified designs: the powered tools are blocky, the retractor has no teeth, there's no detailed text or markings, the drill has no Jacobs-chuck detail.
   - The needle-driver and forceps arms are rigid parts with pivots, not deforming.
   - Box colliders are approximate.
   - `robot.arm` is not built (Person 1 has a robot).
   - Measuring and alignment rods are not built.
   - `inst.forceps` and `inst.suction` are proposed ids, not yet in the contract.
10. **Materials and lighting.**
    - URP Lit only. Metals need a reflection probe, so the test scene bakes one.
    - Prepped skin is a uniform povidone-iodine tint. There's no wet look and no sheen variation.
11. **Performance.**
    - Measured only in the Editor game view.
    - The knee is about 184k triangles with no LODs (it is close-range hero geometry).
    - VR frame rate on the VIVE Focus 3 has not been measured.
12. **Not verified on hardware.** No VIVE Focus 3 test. Person 1's room was checked only in the Editor, not on the headset and not in `OperatingRoom.unity`. No integration with Person 3's mechanics.
