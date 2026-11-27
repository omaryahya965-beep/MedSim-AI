# Known Limitations (v0.5, 2026-11-27)

**v0.5 notes.**
- **Embodied actions are procedural** (IK and posture layers), not motion capture. Legs never move, since the patient stays under the blanket.
- **The default speech model sometimes mishears short colloquial Arabic commands.** Seen once: «عمو، ممكن تقعد شوي؟» was heard as a request to be examined. Repeating the request or using `gpt-realtime-2.1` helps.
- **No new visual-realism work in v0.5:** skin, hair, eyes, clothing and hands are unchanged.
- **The centre interaction prompt is English** in Arabic mode.
- **Echo filter:** a student who deliberately repeats the patient's exact words within 60 s would be ignored.

**v0.4 notes.**
- **Arabic quality:** the default understanding model (`gpt-realtime-2.1-mini`) still makes occasional slips in the doctor's Arabic and small transcription errors. `MEDSIM_VOICE_UNDERSTAND_MODEL=gpt-realtime-2.1` is better but slower (+0.6–1 s) and about 9× the cost per turn.
- **Debrief rubric items stay English.** In an Arabic session the rubric item names are English, so the written report lists them in English.
- **VR thumbstick movement, the new +0.55 m height and the headset microphone in Arabic** are not yet tested in the VIVE Focus 3.
- **Language-command detection uses phrase patterns** (Arabic and English) on short utterances. Unusual phrasings may be treated as a normal turn; say «احكي عربي» or "speak English" plainly.

## Not yet verified
1. **No headset test of v0.3.** Everything was verified on desktop, in Editor Play mode, or in the standalone Desktop exe. With SteamVR and VIVE Streaming running, the PCVR exe started OpenXR and chose VR ("headset display running"). Whether the image and controls work in the VIVE Focus 3 has **not** been checked by a person.
2. **Clinical content is DRAFT — NOT CLINICALLY VALIDATED.** This includes:
   - the new patient states and transitions,
   - the supervisor rules,
   - the reasoning concepts,
   - the AI persona briefs,
   - the draft Arabic translations.
3. **Arabic:** pronunciation and dialect quality have not been judged by a native speaker. The supervisor sometimes answers in formal Arabic rather than Palestinian dialect.

## Live AI
4. **Latency (v0.3 native voice).**
    - End of speech to first audio: median about 1.9 s, range 1.6–2.9 s (see NATIVE_VOICE.md). Previously 4.5–5.5 s.
    - Long, multi-clause replies are the slow ones.
    - Typed questions still use the text path: about 3 s, then streamed speech for character-initiated lines.
    - Hands-free mode adds the 0.65 s end-of-speech window.
5. **Exact-text fallback deadline.** The fallback uses `/v1/audio/speech` (`gpt-4o-mini-tts`), which shuts down on **2027-01-06**. After that the fidelity check can only retry with Realtime.
6. **Grounding has limits.** The deterministic checks cover numbers, a clinical lexicon, diagnosis words, dosing and meta-talk. Paraphrased Arabic clinical wording is only partly covered by the Arabic lexicon. Anything that cannot be checked is labelled AI-interpreted in the debrief; nothing is silently scored.
7. **The doctor can be too helpful or wordy.**
    - She occasionally adds a teaching point. For example, when the student asked for an ECG she added "we wait for the result before changing management".
    - v0.3 added a rule ("teach by asking; never advise waiting for, delaying or withholding treatment, results or escalation"), but replies are model-generated within the grounding rules.
    - Clinical review is still recommended.
8. **Spending.** The $2 test budget cap is in-app. **About $1.16 remains** after v0.3 testing ($0.84 used in total). A demo session costs roughly $0.05–0.08; a voice turn about $0.0025.
9. **The API key was exposed in a chat transcript.** Rotate it.

## Visuals and characters
10. **Characters are Rocketbox quality,** much better than MakeHuman but not photoreal scans.
    - Skin uses URP Lit without subsurface scattering.
    - Hair is cards.
    - The patient has no chest skin under the gown. The legs, jeans and shoes are no longer rendered: v0.3 removed them, since they are always under the blanket.
    - His left elbow sits slightly out to the side.
    - Eye bones are not mapped; gaze uses blend shapes.
11. **Animation gaps:**
    - The doctor's observing idle is a weight-shift clip; there is no arms-crossed clip yet.
    - There is no pointing clip; pointing is a procedural arm aim.
    - The patient clips were hand-keyed in Blender.
12. **Lip sync.**
    - Calibrated (v0.3) on the `cedar` and `marin` voices: vowels A, I, U, E, O, plus loudness.
    - Calibration used synthetic sustained vowels; the silence and S entries keep the sample data.
    - There are no consonant visemes beyond what uLipSync infers from vowels and S.
13. **Environment:**
    - The layout is not mirrored; the student's working side is still an open clinical decision.
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

## Desktop Mode
19. **PCVR build without a headset.** If SteamVR is the active OpenXR runtime and no headset is connected, the OpenXR start-up call can block inside SteamVR. v0.3 adds a recovery path:
    - A watchdog relaunches the app in Desktop Mode after 45 s.
    - A launch whose XR start-up never finished makes the next launch start in Desktop Mode, with a notice.
    - The watchdog relaunch itself has not been triggered on real hardware.
    - The Desktop build (OpenXR excluded) remains the reliable choice without a headset.
20. **VR start-up path untested on hardware.** VR Mode now starts XR manually instead of "Initialize XR on Startup", and this has not been verified on the VIVE Focus 3. If it fails, re-enable that checkbox (Windows) and rebuild.
21. **Desktop interaction is simplified to point-and-click.** Pick up, aim and apply; there are no hand poses.
22. **Microphone with a real human voice is untested.** Tested with synthetic clips, including an acoustic loopback through the laptop speakers into the laptop microphone in the exe. Please run the checklist in NATIVE_VOICE.md. In hands-free mode with loud laptop speakers, echo gating is tuned but not proven for every room; use push-to-talk or headphones if you get stray turns.
23. ~~Blanket artifacts~~: fixed in v0.3. The legs are removed and the blanket is generated from the pose.
24. **Native voice is English and Palestinian Arabic only.** Other languages are not prompted for.
25. **Hands-free VAD is energy-based** (adaptive noise floor), not a neural VAD. Steady loud background noise can delay the end of a turn. The 15 s cap still applies.
26. **Desktop exe self-test** (`-medsimSelfTest`) moves the player and plays a loud test clip. It is for verification only and never runs without the flag.
