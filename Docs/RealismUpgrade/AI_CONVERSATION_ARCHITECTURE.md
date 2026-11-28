# AI conversation architecture (as implemented)

## Who answers what
```
student speech (T / A-X / hands-free)
   -> native Realtime voice turn (proxy) -> heard text + reply
        - hospital navigation intent? -> deterministic guide action (walk, wait, walk with me); short authored line
        - in Room 02                  -> surgical route (/v1/surgery/*): surgeon or assistant, grounded in OR_TKA_TRAINING_001
        - otherwise                   -> clinical route: doctor or patient, grounded in ER_CHEST_PAIN_001
```
- **Same pipeline as v0.6.** Native Realtime; no extra speech-to-text step. Typed questions still work.
- **Listener.** In the hospital the patient is addressable only inside Room 01; elsewhere the student talks to Dr. Hassan.

## Context the AI receives
- **Clinical:**
  - stage, patient state, the findings the student has actually obtained (readings appear only after a completed measurement), discussion state;
  - **new:** `location` (reception | corridor | room01 | room02; anything else is dropped).
  - Outside Room 01 the doctor answers as the hospital guide (proxy `outsideRoomNote`).
- **Surgical:** current objective, completed objectives, rejected attempts, instruments and their availability, hand-over state. Unity re-validates every action.

## Repetition control
- **Hospital welcome:** once per visit.
- **Room introductions:** once per visit. Re-entering gets `room01_again` / `room02_again`; the scenario's own rule is copied, never modified.
- **Guide arrival line:** shortened, so the surgeon's welcome is the only introduction at the operating room.
- **Surgical supervisor rules:** keep their authored cooldowns and `maxTimes`. Assessment mode removes unsolicited hints and keeps safety lines.

## Language
- Auto mode follows the language the student speaks, in every room (Room 02 now too). An explicit choice ("speak English", «احكي عربي», the menu) locks it.

## Not yet implemented
- Memory that spans rooms.
- Partial (streaming) transcripts and a scrolling history.
- Location context for the surgical route (the surgical context is already room-specific).
