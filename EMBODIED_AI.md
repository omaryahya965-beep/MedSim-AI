# Embodied AI: physical character interaction (v0.6, 2026-11-27)

> Clinical content remains **DRAFT — NOT CLINICALLY VALIDATED**. The VIVE Focus 3 headset has **not** been tested with v0.6: everything below was verified on desktop (Editor Play mode and the standalone Desktop exe).

The student can ask Mr. Reyes (patient) and Dr. Hassan (supervisor) to do physical things in natural Palestinian Arabic, Modern Standard Arabic, English or a mix. The character answers and does it in the same moment, holds the pose until told otherwise, and never claims to have done something it did not do.

The conversation panel from v0.5 is unchanged.

---

## 1. How an instruction becomes a movement

```
student speech / text
   │
   ▼
proxy  ── understanding (same AI call as the reply; forced JSON schema)
   │       studentAct / intent = physical_request, and actions[] (max 3) from a FIXED catalogue
   ▼
validateAction()  (Backend/medsim-proxy/lib/actions.mjs) — deterministic, no AI
   │  • catalogue check (anything else → "what would you like me to do?")
   │  • scenario actionPolicy: patient.alwaysDenied = [stand_up, walk] → declined + supervisor rule I_UNSAFE_MOBILISE
   │  • side: raise/turn without a side → "Which arm, doctor?"; "bring your hand closer" → the arm nearest the student
   │  • unambiguous verbs fix a mislabelled arm action (ارفع/raise → raise_arm, مدّ/قرّب/extend → extend_arm)
   │  • one unsafe step declines the whole sentence (no half-done "sit up and walk")
   ▼
reply text chosen to match: the AI's line, or the authored refusal / clarification, or «حاضر.» / "Okay."
   ▼
Unity  CharacterActionRouter (one queue per character) → PatientActionController / DoctorActionController
   │  Requested → Validated → InProgress → Completed | Interrupted | Unavailable   (or Rejected / NeedsClarification)
   ▼
result POSTed back (/v1/action-result) → the AI's next prompt says what REALLY happened
```

The AI only proposes actions from a fixed list. Nothing it returns is executed as code. Only the deterministic validator decides what runs, together with the authored scenario policy.

### Action states (`CharacterActionRouter.ActionState`)

| State | Meaning |
|---|---|
| Requested | Received from the turn |
| Validated | Accepted by the proxy and waiting in the character's queue |
| InProgress | The body is moving |
| Completed | The pose was reached (measured on the body, e.g. arm weight ≥ 0.95, sit ≥ 0.95, breathing depth > 3) |
| Interrupted | A new instruction to the same character replaced it ("actually, lie back") |
| Rejected | Declined by the scenario policy (stand / walk) |
| NeedsClarification | Missing side or not in the catalogue; nothing moves |
| Unavailable | The body or NavMesh could not do it (e.g. no reachable spot) |

Every state change is recorded:
- `ACTION_REQUESTED` and `ACTION_RESULT` go to the session log;
- the action history is kept in `CharacterActionRouter.History`;
- the final state is posted to the proxy (`actionContext`).

Requests and results are **never scored**. Exam scoring is unchanged: it comes only from real examination events (instrument on the right zone, auscultation dwell, and so on). Asking the patient to raise his arm earns nothing; measuring the blood pressure on that arm does.

### Several instructions at once
"اقعد شوي وارفع إيدك الشمال" → `sit_up` then `raise_arm:left`. They run in order: the arm starts only after he has sat up. Up to 3 actions per sentence.

---

## 2. Supported actions

### Patient (`PatientActionController`)
| Action | Example phrasings (Arabic, MSA, English, mixed) | Body |
|---|---|---|
| sit_up | «اقعد شوي», «اجلس قليلاً», "sit up" | Backrest and torso rotate 24° together about the spine pivot; held |
| recline | «ارجع تمدد», "lie back" | Back to the resting angle |
| raise_arm (L/R) | «ارفع إيدك الشمال», «ارفع يدك اليمنى», «ممكن ترفع your left arm؟» | Two-bone IK, forward and up in front of the chest; **held** |
| extend_arm (L/R/nearest) | «مد إيدك اليمين», «قرّب إيدك علي» (no side → nearest arm) | Held out towards the student. The far arm stays on its own side and never reaches across the chest |
| lower_arm | «ريّح إيدك», «نزّل إيدك» | One or both arms back to rest |
| turn_to_side (L/R) | «لف شوي على جنبك الشمال» | Small torso twist and roll |
| shift_on_bed | «تحرك شوي لفوق عالتخت» | Settle/adjust motion |
| deep_breath / breathe_normally | «خذ نفس عميق», «تنفس عادي» | Visibly deeper and slower breathing until told to stop (max 30 s) |
| open_mouth / close_mouth | «افتح ثمك», «سكّر ثمك» | Jaw, held up to 15 s |
| look_at_student | «طلع فيي» | Eye contact for 8 s |
| point_to_pain | «فرجيني وين الوجع» | Right hand to the sternum (the authored pain site), 5 s |
| hold_still | «خليك ثابت» | Stops idle shifts and breathing sway for 12 s |
| rest | — | Everything back to rest |
| **stand_up, walk** | «قوم امشي معي» | **Always declined:** «معلش يا دكتور، ما بقدر أقوم هلّق، الوجع كتير.» plus a supervisor reminder |

The patient also does some things on his own:
- **Auscultation:** while the stethoscope is on a chest zone (held on the patient, or within 7 cm of a chest point), he keeps still.
- **Deterioration:** when he enters `S_DETERIORATION`, held poses end and he rests.
- **Idle life:** small shifts and moments of discomfort, but never during speech, an action, or a hold.

### Doctor (`DoctorActionController`, NavMesh walking, never teleports)
| Action | Example phrasings | Where she goes |
|---|---|---|
| come_to_student | «دكتورة تعالي هون» | About 1 m from the student, never between student and patient |
| stand_beside_student | «وقفي جنبي» | Beside the student |
| approach_patient | «تعالي شوفي المريض» | Bedside, not on the student's side |
| step_back | «ممكن تبعدي شوي عشان أفحص المريض؟» | Further away, still in view |
| go_other_side | «تعالي من الجهة الثانية», "come to the other side of the bed" | The opposite bedside (±0.95 m from the patient) |
| observe_from_distance | «ممكن تتركيني أفحص المريض لحالي؟» | The doorway observation mark («رح أراقبك من بعيد») |
| wait / return_to_observation / look_at_patient / look_at_student | «استني شوي» | — |
| demonstrate_instrument | «فرجيني كيف أستخدم جهاز الضغط» | Turns to the named instrument, points at it and explains with a gesture (she does not pick it up) |

The student-relative spots (come_to_student, stand_beside_student) keep at least 1.05 m from the bed centre and are never between the student and the patient. Bedside moves (approach_patient, go_other_side) stand about 0.95 m from the patient. The student's body is a NavMesh obstacle, so she walks around you.

### Social gaze (`ActiveSpeakerAwareness`)
- When the patient speaks, the doctor looks at him.
- When the doctor speaks, the patient glances at her.
- Otherwise both attend to the student.
- An action's gaze (e.g. "look at the patient") has priority.

### Arabic intent classes
Every turn is classified first. The patient's `studentAct` and the doctor's `intent` both include:
- `physical_request`;
- medical question;
- help or hint request;
- language command (no AI call);
- diagnosis or escalation;
- emotional support.

A symptom question that sounds like movement («رجع الوجع لما مشيت؟», "did the pain come back when you walked?") is **not** an action. This is checked by a proxy test and by the live harness.

---

## 3. Animation: what was evaluated and what is used

| Option | Licence / cost | Decision |
|---|---|---|
| Unity **Animation Rigging** (TwoBoneIK, MultiAim) | Free Unity package | Not installed. The same analytic two-bone IK (law of cosines plus elbow pole) is implemented in `PatientActionController.SolveTwoBone`: no new dependency, about 0.02 ms per frame |
| **Final IK** / other Asset Store IK | Paid | **Not purchased** (no approval for paid assets) |
| **Mixamo** clips | Free with an Adobe account | Not used: no in-bed motions (sitting up against a backrest, arm raise while reclined), and downloading needs an interactive Adobe login |
| **CMU / other mocap** | Free (CMU: free for any use) | Not used: no bed-supported sequences; retargeting a standing clip onto a reclined, blanket-covered body gives clipping |
| Procedural layers (chosen) | Our code | Runs after the Animator: posture (spine plus backrest rotating together), arm IK, jaw, breathing depth and rate, gaze, holds. Critically damped smoothing, so there are no pops; every pose is measured for completion |

Execution order is Animator → `PoseRefinement` (-50) → `PatientActionController` (-45) → `HumanoidLifeController` (0).

Instruments ride on the bones: the cuff, oximeter and stethoscope zones are parented to the arm, finger and chest bones, and Rigidbody interpolation is off while attached. In the exe, the cuff stayed 0.000 m from its snap point while the arm rose, and the oximeter likewise on the extended finger.

The blanket and lower body are unchanged and believable during all actions: the legs stay under the procedural drape, and only the torso and arms move.

### Surface realism (v0.6)
Menu *MedSim AI/Polish/3. Fabric and skin micro-detail*. The textures are generated in-project, with no third-party assets:
- `Characters/v2/Textures/Generated/Fabric_Weave_N.png`: plain-weave cotton normal map on the gown (scale 0.38, 13×13 tiling).
- `Skin_Pores_N.png`: pore and micro-relief detail normal on both characters' heads and bodies (URP Lit detail map, 14×14).

Before/after close-ups are in `Screenshots/Realism/compare_*.png`. They were captured at the first, stronger gown setting (0.55, 9×9); the final setting is subtler.

---

## 4. Test results (v0.6)

| Check | Result |
|---|---|
| Unity EditMode | **98 / 98** |
| Unity PlayMode | **29 / 29**. New `EmbodiedQueuePlayTests`: multi-action order plus a rejected unsafe step; a new instruction interrupts; clarify doesn't move; nearest arm (both sides); deep breath until "breathe normally"; deterioration ends held poses; doctor to the other side and observing from a distance |
| Proxy | **34 / 34**. New: multi-action, nearest arm, an unsafe step blocks the sentence, new doctor actions, a symptom question is not an action, arm-verb correction with a matching reply |
| Natural instructions, real API, typed (`tools/action-harness-v06.mjs`) | **17 / 17**, including multi-step Arabic, MSA and English, mixed «ممكن ترفع your left arm؟», «تعالي من الجهة الثانية», «لحالي», «اقعد وبعدين قوم امشي» (declined) |
| Spoken instructions, real Realtime voice (`tools/voice-action-check.mjs`, synthetic student clips) | «ارفع يدك الشمال» → raise_arm:left ✔. The English history question has no action ✔. «عمو، ممكن تقعد شوي؟» was misheard («تقطش») → the patient asked for clarification and did not guess (safe, but not executed) |
| **Standalone Desktop exe** (`-medsimSelfTest`) | sit_up, raise_left_arm (cuff 0.000 m), extend_right_arm, deep_breath, doctor come_to_student and step_back: all **Completed**. A spoken Arabic instruction was executed 3.1 s after it was sent |
| Performance in the exe (RTX 3050 Laptop 4 GB, 1920×1080, vSync on a 240 Hz panel) | Corridor 191 fps (p95 8.3 ms); room 200 fps; **during character actions 214 fps, avg 4.66 ms, p95 8.33 ms, p99 8.34 ms**; patient action layer **0.021 ms/frame**; GPU utilisation avg 57 %; peak VRAM 1.7 GB (the Unity Editor was also open) |
| Speech latency (exe and proxy log, Realtime path) | End of speech to first audio 1.9–2.6 s for normal turns (understanding about 1.2–1.5 s). A corrected-action acknowledgement took 3.9 s once |
| Secret scans | Desktop and PCVR builds clean |

### Visual evidence (`Screenshots/Embodied/`)
GIFs (about 10 fps) and 1280×720 stills, rendered from mocked validated actions (no API cost):
`01_resting_reclined`, `02_sit_up`, `03_raise_left_arm_bp_cuff`, `04_extend_right_arm_pulse_oximeter`, `05_deep_breath`, `06_auscultation_holds_still`, `07_doctor_walks_to_student`, `08_doctor_moves_aside`, `09_doctor_other_side_of_bed`, `10_speech_and_movement` (player view with the panel).

Exe screenshots (`exe_action_*.png`, `exe_voice_action.png`) are written by the self-test to `%USERPROFILE%\AppData\LocalLow\MedSim AI\MedSim AI\selftest\`. Three are copied to `Screenshots/Embodied_exe/`.

Regenerate with `AgentScripts/EvidenceCapture.cs` (Play mode eval), then `AgentScripts/make_gifs.ps1`.

### Bugs found and fixed during v0.6 testing
- **Same-frame first action.** If the router created the controller and ran an action in the same frame, `Start()` had not run: "nearest arm" picked the wrong arm, and the doctor had no movement component. Both controllers now initialise on first use.
- **Wrong arm action from speech.** The mini speech model returned extend_arm for «ارفع إيدك», and the reply described extending. A deterministic verb correction now applies, with a neutral «حاضر.» so the speech never contradicts the movement.
- **Far arm crossing the chest.** The far arm reached across the chest towards a student on the other side. It is now held out in front on its own side.

---

## 5. Limitations
- Motion is procedural, not motion-captured. The legs never move (the patient is bedridden under the blanket).
- The mini Realtime speech model still mishears some short colloquial commands. The system then asks instead of guessing. `MEDSIM_VOICE_UNDERSTAND_MODEL=gpt-realtime-2.1` is better but about 9× the cost, so it was **not** switched without approval.
- `point_to_pain` always uses the right hand to the sternum (the authored central chest pain). Other scenarios would need their own pain site.
- The doctor's `demonstrate_instrument` is a point-and-explain cue, not a hands-on demonstration.
- Patient answers can refer to what happened earlier in the session. For example, after a declined "walk with me", «رجع الوجع لما مشيت؟» was answered «ما مشيت، ما قدرت أقوم». This is consistent with the session, but it is AI wording, not an authored fact.

## 6. API cost (v0.6)
| Run | Cost |
|---|---|
| Typed action harness, 17 turns | $0.0042 |
| Exe self-test (welcome line, voice turns, spoken action) | $0.0411 |
| Spoken action check, 3 clips | $0.0087 |
| **v0.6 total** | **≈ $0.054** |

The proxy ledger now shows **$1.905 spent of the $2.00 hard cap, $0.095 remaining**. That is lower than the ~$0.62 expected; the ledger is computed from provider-reported token usage, and the OpenAI dashboard is the authority. The budget was not reset or raised.

## 7. Manual checks
**Desktop:**
1. Open the door.
2. Hold **T** and say «عمو، اقعد شوي وارفع إيدك الشمال».
3. Put the cuff on that arm.
4. Say «ريّح إيدك», then «دكتورة، تعالي من الجهة الثانية», then «ممكن تتركيني أفحص المريض لحالي؟».

**VR (not yet done):** the same in the VIVE Focus 3, plus checking that the doctor never blocks the bedside while you hold an instrument.
