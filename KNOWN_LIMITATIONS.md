# Known Limitations (v0.2, 2026-11-27)

## Not yet verified
1. **No headset test.** Everything was verified on desktop or in Editor Play mode. The built PCVR player stops at OpenXR start-up on this PC when no headset is connected, so test it with the VIVE Focus 3 connected through VIVE Business Streaming or SteamVR.
2. **Clinical content is DRAFT — NOT CLINICALLY VALIDATED.** This includes:
   - the new patient states and transitions,
   - the supervisor rules,
   - the reasoning concepts,
   - the AI persona briefs,
   - the draft Arabic translations.
3. **Arabic:** pronunciation and dialect quality have not been judged by a native speaker. The supervisor sometimes answers in formal Arabic rather than Palestinian dialect.

## Live AI
4. **Latency.** About 3 s from a typed question, or 4.5–5.5 s from the end of speech, to the character's voice. Streaming playback is not implemented, although Realtime's first audio arrives at about 0.7 s. That is the next improvement.
5. **Exact-text fallback deadline.** The fallback uses `/v1/audio/speech` (`gpt-4o-mini-tts`), which shuts down on **2027-01-06**. After that the fidelity check can only retry with Realtime.
6. **Grounding has limits.** The deterministic checks cover numbers, a clinical lexicon, diagnosis words, dosing and meta-talk. Paraphrased Arabic clinical wording is only partly covered by the Arabic lexicon. Anything that cannot be checked is labelled AI-interpreted in the debrief; nothing is silently scored.
7. **The doctor can be too helpful.** She occasionally hints at a teaching point early, e.g. "without waiting for troponin" in reply to "Is this a STEMI?". Prompt tuning and clinical review are recommended.
8. **Spending.** The $2 test budget cap is in-app. About $1.82 remains after testing. A demo session costs roughly $0.05–0.08.
9. **The API key was exposed in a chat transcript.** Rotate it.

## Visuals and characters
10. **Characters are Rocketbox quality,** much better than MakeHuman but not photoreal scans.
    - Skin uses URP Lit without subsurface scattering.
    - Hair is cards.
    - The patient wears jeans and shoes under the blanket, and has no chest skin under the gown.
    - His left elbow sits slightly out to the side.
    - Eye bones are not mapped; gaze uses blend shapes.
11. **Animation gaps:**
    - The doctor's observing idle is a weight-shift clip; there is no arms-crossed clip yet.
    - There is no pointing clip; pointing is a procedural arm aim.
    - The patient clips were hand-keyed in Blender.
12. **Lip sync:** uLipSync uses the sample profile, which detects vowels and S only. It is not calibrated to the `marin` and `cedar` voices.
13. **Environment:**
    - The layout is not mirrored; the student's working side is still an open clinical decision.
    - The north-west corner behind the curtain is dark.
    - Vision panels are opaque.
    - The door closer is decorative.
    - The notice-board label is faint.
    - The monitor and ECG screens show only at runtime.
14. **Doctor navigation:** if she is sent before the door opens, her NavMesh path goes through the closed door. The flow avoids this by moving her only after `DOOR_OPENED`.
15. **Builders conflict.** Re-running the old `MedSimSceneBuilder` restores the MakeHuman characters, so after it run *Build Redesign (v0.2)* and then *Characters v2*.

## Interaction
16. **Hands:** XRI controllers with instrument snap zones. Hand-tracking and physics hands (Auto Hand / Hurricane VR) were not added.
17. **ECG:** the cart's RECORD button records it; electrodes are not placed one by one.
18. **Room entry:** the room-entry event requires walking through the doorway. Teleporting past the trigger skips it, which does not affect the flow.
