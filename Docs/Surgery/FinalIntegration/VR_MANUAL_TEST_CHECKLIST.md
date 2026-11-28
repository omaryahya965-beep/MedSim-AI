# VR manual test checklist: integrated hospital (VIVE Focus 3, PCVR)

**Status:** none of this has been performed. No headset was available during the integration. The PCVR build exists and starts, but **VR behaviour is unverified.**

## Setup
- Build: `D:/Medical-Test/Builds/MedSimAI_Integrated_PCVR/MedSimAI.exe`.
- Headset: VIVE Focus 3 through VIVE Business Streaming (or SteamVR). Set that runtime as the active OpenXR runtime.
- Start the AI proxy if a live-voice test is authorised and budgeted. Otherwise test text-only, and record which mode you used.
- Record for each item: Pass / Fail / Not tested, with notes and a headset screenshot or video where possible.

## A. Start and comfort
- [ ] App starts in the headset without errors. `xrCreateInstance` succeeds in `Player.log`.
- [ ] Student starts in the reception lobby at the right height (pause-menu height adjustment works).
- [ ] Locomotion: left stick moves, right stick snap-turns or teleports; corridor and OR teleport floors work.
- [ ] No discomfort from the doctor's walking pace or from automatic doors closing on the player.
- [ ] Frame rate: note the VIVE streaming overlay or SteamVR frame timing in the lobby, the corridor, Room 01 and Room 02 (surgeon view). Note dropped frames when the guided walk starts (the desktop build showed one ~100 ms hitch).

## B. Doctor and navigation
- [ ] The welcome is spoken (or subtitled) and readable. The lobby directory is readable at arm's length.
- [ ] The directory buttons respond to the XR ray.
- [ ] Voice "take me to the operating room" / «خديني على غرفة العمليات» (hold A/X) starts the guided walk.
- [ ] She waits when you fall behind; «استني» / "wait" and «امشي معي» / "walk with me" work.
- [ ] OR sliding doors open as you approach and do not trap or clip the player.
- [ ] With the exam-room door closed, a request that needs it is declined truthfully.

## C. Room 02: surgical training
- [ ] The surgical session starts on entry. The HUD banner and objective text are readable in the headset and do not cover the surgical site.
- [ ] The conversation panel does not overlap the HUD.
- [ ] Each tray instrument can be grabbed with grip. It follows the hand without jitter and is not grabbed twice (one interactable each).
- [ ] Skin marker on the right knee: marking appears. On the left knee: wrong-side safety line.
- [ ] Scalpel, retractor, femoral guide then saw, tibial guide then saw: tissue states change visibly. Note how the powered saw is switched on in VR (the desktop uses E; the VR binding is unverified).
- [ ] **Accidental touches:** brushing a target with the wrong tool registers a rejected attempt. Note how often this happens and whether it feels unfair.
- [ ] Trials, components, tray and insert can be placed by hand. Seating feedback is clear. Impactor and mallet complete the objectives.
- [ ] The robot arm moves to the resection target during resection and never blocks the student's hands.
- [ ] Ask Sami for an instrument. He fetches it and holds it out. The hand-over completes **only when you grab it** (check the HUD/log).
- [ ] Help request (voice), assessment mode (F2 is desktop-only, so use the directory), and the debrief is readable.
- [ ] Restart from the pause menu: everything resets.
- [ ] Leaving through the doors ends the session, and the HUD disappears in the corridor.

## D. Room 01: clinical examination (v0.6 regression)
- [ ] Door lever grab and push open.
- [ ] Cuff, oximeter and stethoscope grab and apply; ECG record button.
- [ ] Patient voice conversation and physical requests (raise arm, sit up).
- [ ] Leaving mid-case ends the session as "left unfinished". Re-entering starts clean.

## E. Language and voice (real human voice)
- [ ] Arabic speech is recognised in the corridor, in Room 01 and in Room 02, and the language persists across rooms.
- [ ] English speech likewise; a mixed sentence.
- [ ] Interrupting the doctor while she speaks.
- [ ] Changing room while she is speaking or walking (not covered by automated tests).

## F. Sign-off
| Tester | Date | Headset / runtime | Mode (live AI / text-only) | Result |
|---|---|---|---|---|
| | | | | |
