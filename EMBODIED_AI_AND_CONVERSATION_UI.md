# v0.5: Conversation panel and embodied AI (physical actions)

Stable recovery point:
- git tag **`v0.4-stable`** (commit `9ccc8f6`);
- both previous builds copied to `D:\VRBuilds\Backup_v0.4_9ccc8f6\`.

## 1. Conversation panel (the overlapping-text fix)

### Root cause

- The panel was a fixed-height bar split into fixed percentage bands: name 80–100 %, response 36–82 %, "You:" 0–36 %.
- Nothing measured the text, and the text had no overflow limit, so a long reply simply drew past its band, over the transcript and outside the panel.
- Desktop Mode also squeezed the bar to 190 px.

A second cause was in the Arabic font itself. NotoSansArabic declares about **2.1 em** of ascender + descender per line, against 1.15 em for Latin. Any line containing Arabic was therefore about 54 px tall instead of about 29 px. That is why Arabic took so much room, and why the Arabic speaker name, taller than its row, could disappear.

### What changed

- **Fixed-size panel with measured rows (top to bottom):**
  - header: speaker name, plus the status «أستمع إليك… / يفكّر… / يتحدّث» or "Listening…/Thinking…/Speaking" on the opposite side;
  - character reply: up to 3 lines;
  - a thin divider;
  - your transcript: up to 2 lines.
  The panel never resizes while speech streams in, and no text can leave its row.
- **Long text is paged, not clipped.**
  - The reply's pages advance in step with the speech, and a small "1/2" shows on multi-page replies.
  - The transcript pages rotate every 4 s.
- **Arabic metrics:** the Arabic UI font's metrics were normalised to 1.42 em lines. Arabic lines are now 35 px.
- **Arabic layout:**
  - right-aligned and right-to-left, with joined letters;
  - English words, numbers and % read correctly inside Arabic;
  - localised names: «السيد آدم رييس (المريض)», «د. سارة حسن (المشرفة)»;
  - «أنت:» label and Arabic status words.
- **English layout:** left-to-right, unchanged wording.
- **Speaker echo:** the "You:" line in your screenshot contained the patient's own words. The microphone had picked up his voice from the speakers. The proxy now ignores a "student" transcript that repeats a character's recent line (at least 60 % of 4+ words).

### Verified (rendered screenshots plus programmatic checks)

Each case was checked for every visible character being inside its row and for no overlap between the reply and transcript rows.

| Case | Result |
|---|---|
| Short Arabic reply | 1 page, inside, right-to-left, right-aligned |
| Long Arabic reply (5 sentences) and long Arabic transcript | 2 pages; page 2 shown mid-speech; transcript 2 pages; inside; no overlap |
| Mixed «ECG… 152/94… 96%… troponin» and «12-lead ECG … SpO2 (هلّق)» | Inside; terms and numbers readable |
| Long English reply and transcript | 2 pages, left-to-right, inside |
| **VR world-space panel** (PlayMode test) | Long Arabic and English inside their rows; rows never overlap |

Screenshots are in `Screenshots/ConversationUI/` (`*_panel.png` are crops) and `Screenshots/Actions/voice_02_raise_left_arm_during.png` (live).

## 2. Embodied AI: the characters do what you ask

### How it works (safe by construction)

```
your words (voice or typed, any language)
  → Realtime / text model proposes ONE action from a fixed catalogue: {type, side, instrument} plus a short acknowledgement
  → deterministic validator (proxy, lib/actions.mjs) against the authored scenario policy (scenario.actionPolicy)
      accepted → the acknowledgement is spoken ("حاضر" / "Sure") while Unity executes the action
      denied   → approved refusal line (e.g. «معلش يا دكتور، ما بقدر أقوم هلّق، الوجع كتير.»), and the supervisor
                 rule I_UNSAFE_MOBILISE may step in («خلينا نخلّي السيد رييس مرتاح على السرير وعلى المونيتور…»)
      clarify  → «أي إيد يا دكتور، اليمين ولا الشمال؟» / "Which arm…?"
  → Unity: CharacterActionRouter → PatientActionController / DoctorActionController
  → result (completed / interrupted / unavailable) POSTed back (/v1/action-result) and shown to the AI as
    "what really happened", so a character never claims an action that did not happen
```

- No AI-generated code runs, and the AI cannot modify the scene. Only catalogue actions with validated parameters reach Unity.
- Requests and results are recorded as `ACTION_REQUESTED` and `ACTION_RESULT` events for the debrief. They are **not scored**. Clinical scoring is unchanged.
- Symptom questions are not physical requests. Asking «وين بالزبط الوجع؟» may make the patient *also* point to his chest while answering, which is natural; the fact is still conveyed.

### Patient actions

| Action | What he does |
|---|---|
| sit_up / recline | The bed's backrest and his back rise or recline together (24°); the pillow stays behind his head |
| raise_arm (left/right) | Forward-up in front of the chest (two-bone IK with an elbow pole); held for examination, then rested |
| extend_arm (left/right) | Reaches towards you |
| lower_arm / rest | Back to the resting posture |
| turn_to_side (left/right) | Gentle torso twist and roll, head follows |
| shift_on_bed | Small settling movement |
| deep_breath / breathe_normally | Breathing depth ×3.4 at 7/min for about 13 s, then normal |
| open_mouth / close_mouth | Jaw open for 5 s |
| look_at_student | Holds eye contact |
| point_to_pain | Hand to the middle of the chest (the approved pain site) |
| hold_still | Gaze steadies, no head tilt, for 12 s |
| stand_up / walk | **Always declined** (scenario policy: acute chest pain stays on the bed and monitored) |

- **Instruments follow the body.** Exam zones are parented to the bones, and attached instruments follow exactly: 0.000 m drift measured on the BP cuff while the arm was raised.
- **Instrument drift bug fixed.** Rigidbody interpolation used to pin the cuff in place; it is now disabled while an instrument is attached.
- **Natural idle behaviour** (never during speech, actions or "keep still"):
  - blinking, breathing, gaze and the pained expression, as before;
  - new: a small settling shift every 35–70 s;
  - new: in pain states, briefly bringing a hand towards the chest every 60–110 s.
- **Legs stay under the blanket.** Postures move only the upper body, so the bedridden mesh and draped blanket stay correct and no body part disappears. Lower-body anatomy was therefore not restored. It would only be needed for leg movements, which this scenario does not allow.

### Doctor actions

| Action | Where she goes |
|---|---|
| come_to_student | 1.0 m from you, on the side away from the bed (never between you and the patient) |
| stand_beside_student | 0.8 m to your side, facing the patient |
| approach_patient | The bedside **opposite** you, so she never blocks your examination |
| step_back | Her observation spot, or about 1.1 m away from you and the bed |
| wait / return_to_observation | Stops and faces you / walks back |
| look_at_patient / look_at_student | Turns and holds her gaze |
| demonstrate_instrument | Faces and points at the cuff, oximeter, stethoscope or ECG and explains how it is used, from the approved instrument guidance only; never findings |

She walks on the NavMesh with her walking animation, never teleports, and avoids you: you are a NavMesh obstacle.

### Verified

| Test | Result |
|---|---|
| **Mocked actions** (`AgentScripts/ActionShots.cs`, no API) | All 15 completed with screenshots: sit up (head +0.14 m), raise left arm with BP cuff (hand +0.8 m, cuff drift 0.000 m), lower, extend, point, turn, open mouth, deep breath (13 s), rest; doctor come, beside, approach, step back, demonstrate, return |
| **23 natural instructions, real API** (`tools/action-harness.mjs`) | **23/23 correct.** Palestinian Arabic, English and mixed; "which arm?" asked when no side was given; «قوم امشي معي للباب» declined; symptom questions did not trigger actions |
| **Spoken instructions, real Realtime voice** (`AgentScripts/ActionVoiceE2E.cs`, synthetic clips) | «ارفع إيدك الشمال لو سمحت» heard exactly → «طيب، هيك؟» while the left arm rose; completion reported back. «دكتورة ممكن تبعدي شوي…» → «أكيد، رح أبعد شوي…»; step_back completed. **«عمو، ممكن تقعد شوي؟» was misheard** by the default mini model («مرحبا، ممكن تفحصني شوي؟»), so no action ran |
| **PlayMode tests** | Arm raise with the cuff following, rest, sit up; doctor reaches safe spots and keeps personal space |

The mishearing is a transcription limit of `gpt-realtime-2.1-mini` on short colloquial phrases. The full `gpt-realtime-2.1` transcribes better but costs about 9× per turn.

## 3. Not done or limited in v0.5 (honest list)

- **Animation is procedural** (IK plus posture layers on top of the existing clips), not motion capture. No new animation assets were added or bought. Movements are smooth but simpler than mocap.
- **Visual realism pass not done this round:** skin, hair, eyes, clothing materials, furniture and virtual hands are unchanged. The conversation panel and embodiment took priority.
- **The centre interaction prompt** ("Hold T — talk to…", "E — open the door") is still English in Arabic mode.
- **Untested:** consecutive instruction chains longer than two steps, and interrupting a doctor walk mid-way, were tested only with mocked actions.
- **Needs a person:** the VIVE Focus 3 and a real human voice.

## 4. Manual checklist (10 minutes, desktop or VR)

1. Ask the patient «عمو، ممكن تقعد شوي؟» → he sits more upright with the backrest. (If he mishears, repeat it clearly; see the model note above.)
2. «ارفع إيدك الشمال» → his left arm rises while he says «حاضر/هيك؟». «نزّل إيدك» → it comes down.
3. Put the BP cuff on his left arm, then ask him to raise the arm → the cuff stays on the arm.
4. «فرجيني وين الوجع» → he points to the middle of his chest.
5. «خذ نفس عميق» → you see slower, deeper breathing.
6. «قوم امشي معي» → he politely declines, and the doctor may remind you to keep him on the bed.
7. To the doctor: «تعالي هون», then «ممكن تبعدي شوي عشان أفحص المريض؟», then «تعالي شوفي المريض» → she walks (no teleport), never stands between you and the bed, and goes to the far side of the bed.
8. Long answers: the panel pages (1/2 → 2/2), and nothing overlaps or leaves the panel, in Arabic and English.
