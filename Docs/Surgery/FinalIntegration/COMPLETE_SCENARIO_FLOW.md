# Complete scenario flow

This is the journey a student takes, and the one the exe self-test runs (`hospital_selftest.log`, 76 s, Desktop exe, offline). The times in brackets come from that run.

## 1. Reception (0–12 s)
- The student starts in the reception lobby facing the directory.
- Dr. Sara Hassan welcomes them in the current language. The Arabic welcome is the brief's text. Screenshots `01`, `02`.
- No room session is running.
- The directory shows Room 01 · Clinical Examination, Room 02 · Surgical Training, the mode (Guided / Independent assessment), the language and the DRAFT notice.

## 2. Choosing a room
Any of these works:
- **Voice** (T / A–X), for example «خديني على غرفة العمليات», «بدي أروح على الغرفة التانية», «وين لازم أروح؟», "take me to the operating room", "room one".
- **The directory:** point and click in VR, or click on desktop.
- **Keys 1 / 2 / 3** (desktop).
- **Just walking in.**

An ambiguous request ("both rooms") gets a clarifying question.

## 3. Guided walk (16–21 s)
- The doctor says "Come with me" and walks on the NavMesh. It is a real walk, not a teleport (`04`, `gif_doctor_guides_to_or.gif`).
- If the student lags, she stops and waits. «استني» makes her wait; «امشي معي» switches to walking alongside.
- **A closed door is reported truthfully.** When there is no complete path she says so and does not pretend to move (tested).
- She stops at the OR door, 0.10 m from the mark in the run. The doors open automatically.

## 4. Room 02: surgical training (21–48 s)
On entry the surgical session starts: `mock=False`, runtime `SurgeryMechanicsRoot`, objective 1 `obj.site_confirmation`. Dr. Hassan, as supervisor, introduces it. Sami waits at the back table.

| # | Objective | What the student does | Real effect |
|---|---|---|---|
| 1 | Site confirmation | Skin marker on the right-knee site (scalpel first → **rejected**, safety note; left knee → wrong side) | Marking appears |
| 2 | Skin incision | Scalpel on the incision line | Incision state |
| 3 | Exposure | Retractor on the joint capsule | Capsule opened |
| 4 | Femoral resection | Femoral cutting guide → powered saw (activate with E) | Distal femur → resected (`12`), robot hovers in guidance (`12a`) |
| 5 | Tibial resection | Tibial guide → saw | Proximal tibia → resected |
| 6 | Trial reduction | Both trials seated | Trials seated, then removed |
| 7 | Femoral component | Seat, then impactor | Component fixed |
| 8 | Tibial components | Tray, then insert, then mallet | Tray and insert fixed |
| 9 | Closure | Skin stapler on the wound | Wound closed |

- **Help:** "what do I do now?" or H/J. In guided mode, the supervisor's hint and the target outline.
- **Hand-over:** ask for an instrument («أعطيني دليل القطع»). Sami offers it (`14b`). `assistant.handoff_completed` is published **only after the student's verified `tool.grabbed`**. The run shows `completed-before-grab=False`, then `True` after the grab.
- **Assessment** (`15`): 22/24 (92%), valid 15, invalid 1, hints 1. The spoken summary ends with the clinical-validation reminder.
- **Event bus totals in the run:** 1 `session.started`, 9 `objective.started`/`completed`, 13 grab/release, 1 rejected, 12 state changes, 5 implant placements, 1 `session.completed`.

## 5. Back to the corridor (48–53 s)
- Leaving Room 02 ends its session (`completed`) and records it.
- The doctor offers the other room (`16`).
- Desktop F10 or the pause menu's "Back to the corridor" do the same.

## 6. Room 01: clinical examination (53–75 s)
- «رجعني عالغرفة الأولى» → guided walk to Room 01. The clinical session starts at the door, with the **v0.6 scenario unchanged** (ER_CHEST_PAIN_001).
- The student examines Mr. Reyes with the v0.6 tools. The run asked him to raise his left arm: `raise_arm(left)` Completed (`05`, `06`).
- The tracker held 3 events and **no surgical ids**.

## 7. Leaving unfinished (75–76 s)
- Leaving Room 01 mid-case ends the session as `left unfinished`. The doctor says the case can be restarted at any time.
- Today's sessions are listed on the directory, for example "Room02 OR_TKA_TRAINING_001 completed 92% 0.4 min | Room01 ER_CHEST_PAIN_001 left unfinished".

## Modes
| | Guided training | Independent assessment |
|---|---|---|
| Supervisor hints | Proactive, plus on request | Only on explicit request (counted) |
| Target outline | Yes | No |
| Safety interventions (wrong side, wrong tool) | Yes | **Yes, always** |
| Same simulation and objectives | Yes | Yes |

## Restart
- "Restart" / «من جديد» inside a room, the pause menu, or R in Room 02 restarts that room's scenario only.
- R restores the tissue states, implants and instruments, and objective 1 starts again with its `objective.started`.
