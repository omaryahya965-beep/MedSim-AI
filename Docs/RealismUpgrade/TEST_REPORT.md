# Test report: realism upgrade, increment 1

## Automated (Unity 6000.3.29f1, combined project, offline)
| Suite | Before | Now | New tests |
|---|---|---|---|
| EditMode | 231 | **236 / 236** | 5 parser cases: feminine imperatives inside rooms |
| PlayMode | 42 | **45 / 45** | Walking into Room 01 keeps the door open, and the patient is addressable only inside · re-entering Room 02 gives a short welcome while the scenario stays unchanged · VR contact: only a deliberate tip application counts, brushes are ignored |
| Proxy (`node --test`) | 46 | **48 / 48** | Location context: sanitising, and the guide note in the reception |

Changed tests (behaviour changed on purpose, not weakened): `DesktopModePlayTests.DesktopInstruments_…` and `ScenarioFlowPlayTests.FullSession_…` now press START on the NIBP monitor. One of them asserts that there is **no reading before START**.

## Standalone executable: voice walkthrough (fake provider, no spending)
`MedSimAI.exe -hospitalVoiceTest <dir>` with `Backend/medsim-proxy/tools/fake-provider-server.mjs`:
- The student **walks** with W through the Input System (real collisions).
- They open the Room 01 door with **E**.
- They talk with **T**: the real push-to-talk binding and the **real microphone**.

Run from both deployed builds (`D:/VRBuilds/MedSim_Desktop`, and `D:/Medical-Test/Builds/MedSimAI_PCVR_v0.2` with `-desktop`):

| Check | Result |
|---|---|
| Speech audible (AudioListener output RMS > 0.01) | 9 / 9: reception welcome, reception reply, guide lines, patient (Arabic), Room 02 surgical welcome, surgeon reply (Arabic), assistant, guide back to the corridor |
| Lip sync moving while speaking | Doctor and patient: yes. Sami: no (his mouth is covered by a surgical mask; the model has no blend shapes) |
| Microphone captured per push-to-talk | 1.4–1.9 s every turn (Realtek array, system default) |
| Correct listener | Corridor → doctor (patient not addressable); Room 01 looking at the patient → patient |
| Walking | 14 / 14 legs arrived: reception → corridor → Room 01 (door opened with E) → corridor → Room 02 vestibule → sliding doors → operating position → back |
| Voice navigation | "Take me to room one", «خديني على غرفة العمليات» (from inside Room 01), "Take me back to the corridor" (from Room 02) |
| Room 02 voice | Surgical session online; Arabic question answered aloud; "Sami, pass me the skin marker" led to Sami holding out the marker (`assistant.handoff_offered`) |
| Exceptions | 0 |

Evidence: `Screenshots/Surgery/RealismUpgrade/exe_walkthrough/` (7 screenshots, continuous walkthrough GIF, log, fake-provider log).

## Standalone executable: live check (PAID, real OpenAI Realtime)
`MedSimAI.exe -hospitalLiveSmoke`. Synthetic student clips were played through the speakers into the real microphone.

| Step | Result |
|---|---|
| Reception welcome | Live voice, 14.7 s, audible |
| "Hello Dr. Hassan, nice to meet you." | Heard exactly; the doctor replied aloud |
| Room 01 welcome | Live voice, audible |
| «ارفع إيدك الشمال لو سمحت» to the patient | Heard exactly; he replied aloud in Arabic and **raised his left arm** (action Completed) |
| Cost | **$0.0234**; remaining authorisation **$0.0562** |

The live run exposed two problems, both fixed afterwards and covered by tests:
- in the reception the doctor asked about the patient (no location context);
- the Room 01 welcome said "open the door" to a student already inside.

The fixed wording has **not** been re-checked live.

Evidence: `Screenshots/Surgery/RealismUpgrade/audio_live/` (app output WAV, log, screenshot).

## Not tested
- A human speaking.
- The VIVE Focus 3 (headset microphone and audio output, VR START poke, VR contact dwell).
- Live Room 02.
- Live re-check of the location and welcome fixes.
- Echo suppression with loudspeakers in hands-free mode.
