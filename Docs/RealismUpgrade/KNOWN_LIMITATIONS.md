# Known limitations after increment 1

> **Draft — not clinically validated.**

## Voice and AI
- **Not tested with a human voice, or on the VIVE Focus 3** (headset microphone, audio device, VR controls).
- The live provider was tested in the reception and Room 01 only. Room 02 live voice was tested with the fake provider only.
- The live budget is nearly used: **$0.0562 remains** of the $2.00 authorisation. Further live testing needs a new authorisation.
- **No conversation memory across rooms.** Each room's case runs its own AI session, and corridor conversation is not carried into a case.
- **No partial (streaming) student transcript, and no scrolling history.** The panel shows the latest student line and the current character line, as in v0.6.
- Hospital navigation requests are understood by a deterministic bilingual parser (by design: the doctor must never claim a walk she cannot do). Unusual phrasings fall through to the room's AI, which answers but does not move her.
- The proxy must run for voice: use the launchers. Without it the app shows text only and says so.

## Clinical room
- **Not done:**
  - sleeve or clothing interaction;
  - ECG electrode placement (still the v0.6 RECORD button);
  - stethoscope zone and sound improvements;
  - new patient movement.
- The cuff now needs START. Measurement values always come from the authored scenario.

## Surgery
- Contact rule (VR only): held tool, tip within 3 cm, 0.3 s dwell. These are simulation parameters, not tuned on a headset.
- Tissue changes are still discrete state swaps.
- **Not done:**
  - progressive anatomy transitions;
  - instrument-specific grips;
  - implant rotation control;
  - LODs.
- The assistant has **no reach or grasp animation** (the instrument is held at the hand socket). His mouth is covered by a mask, so there is no visible lip sync.

## Visuals and performance
- **Not done in this increment:** character faces and hands, beds and bedding, drapes.
- **No new performance measurement.** No headset performance measurement.

## Builds
- The integrated builds were deployed to the original folders. The previous v0.6 builds are kept as:
  - `D:/VRBuilds/Backup_v0.6_MedSim_Desktop_20261128`
  - `D:/Medical-Test/Builds/Backup_v0.6_MedSimAI_PCVR_v0.2_20261128`
- The staging folders `MedSimAI_Integrated_Desktop` and `MedSimAI_Integrated_PCVR` still exist and can be deleted.
