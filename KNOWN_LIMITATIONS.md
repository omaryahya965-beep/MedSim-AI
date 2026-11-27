# Known Limitations (after Phase 2)

1. **Characters are realistic MakeHuman humans, not photoreal scans.** They are textured, rigged and have facial blend shapes, but skin shading is standard URP Lit (no subsurface scattering), hair is alpha-cutout cards, and there are no wrinkle maps. Dr. Hassan's lab coat is a retextured CC-BY winter coat (it has a belt), and the gown is a retextured shift dress (sleeveless). No account-free lab coat or hospital gown exists in the MakeHuman packs. Faint shirt poke-through can show at the armpits when the arms are raised.
2. **No keyframed body animation clips.** The physician's standing and gesturing and the patient's reclining pose are authored poses plus procedural layers: breathing, sway, gaze, nodding, explaining and pointing gestures, and facial expressions. Unity Animation Rigging was evaluated but not needed for these constraints. Mixamo clips would require an account.
3. **Lip sync is approximate.** It is text-timed Microsoft visemes scaled by audio amplitude, not phoneme-aligned.
4. **Medical equipment is still procedural geometry** (bevelled meshes with PBR materials). I found no account-free, permissively licensed, high-detail models of a hospital bed, patient monitor, ECG cart or stethoscope (Poly Haven has none; Sketchfab and the Asset Store require accounts). If you can supply licensed models, they can be swapped in.
5. **Live AI is implemented and unit-tested with a fake model, but not called against OpenAI**, because no credentials or billing were authorised. Every OpenAI `/v1/audio/speech` model shuts down on **2027-01-06**. After that, TTS needs another provider or the Realtime API. Arabic support in `gpt-transcribe` is unconfirmed.
6. **Arabic:**
   - Subtitles are shaped and rendered right-to-left (Noto Sans Arabic), but menu and tablet labels stay English.
   - Offline mode has no Arabic voice (Windows has no Arabic SAPI voice installed), so Arabic is subtitles only offline and needs Live AI for speech.
   - The translation is a **draft, not clinically reviewed**.
   - The shaper doesn't handle mixed-direction (bidi) sentences.
7. **Grading** uses deterministic keywords plus, in Live AI mode, transparent AI-intent tags shown in the debrief.
8. **Performance is not measured on hardware.** Baked lighting (2 lightmaps, 90 probes), ASTC textures and fitted skinned bounds help, but each character has 20–28k vertices and around 10 skinned renderers. Android may need LODs or a lower-poly proxy; MPFB can export proxy meshes.
9. The **Android APK has not been built** (it waits on your PCVR approval).
10. **Clinical content remains DRAFT — NOT CLINICALLY VALIDATED.**

## Phase 3 notes
- Most visible pixelation comes from **VIVE Business Streaming** (1152×1152 encode, aggressive foveated compression, 10–20 Mbps), which Unity cannot override. You need to change the VBS and SteamVR settings by hand (see VR_SETUP.md §2).
- Laptop VRAM (4 GB) limits the render scale to **1.1**. HDR is disabled in VR profiles because HDR combined with MSAA crossed the VRAM limit.
- Room lights are baked, so specular highlights from the ceiling panels no longer move with the view; reflection probes give an approximation.
- The characters are still MakeHuman-quality: no subsurface skin, card hair, no wrinkle maps. Phase 3 added detail normals, alpha-to-coverage hair, fabric relief and relaxed hands. Photoreal scanned humans (e.g. Character Creator, MetaHuman-to-Unity pipelines) require licences or accounts.
- **Medical equipment is still procedural.** Poly Haven (checked in Phase 1) has no suitable hospital bed, monitor or ECG models, and Sketchfab, BlenderKit and the Asset Store require accounts. I did **not** search other public-domain sources (e.g. Smithsonian Open Access or NIH 3D) in this phase; that is a possible next step. If you approve specific Asset Store / Sketchfab purchases or downloads, they can be integrated.
