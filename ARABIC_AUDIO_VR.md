# v0.4: Arabic conversation, complete speech, Arabic transcripts, VR height and thumbstick movement

This page covers:
- what was wrong and why (root causes found in the code and confirmed by measurement),
- what changed,
- what was verified,
- what you still need to check with your own voice and the VIVE Focus 3.

## 1. Arabic conversation

### Root causes (why real Arabic conversations failed)

1. **The reply language was fixed before the student was heard.** Every prompt said "Reply in natural spoken English" whenever the app setting was English. Arabic speech therefore got English answers.
2. **The Realtime model sometimes translated Arabic speech into an English transcript.** Measured: «ما تخاف يا عمو…» came back as "You don't have to worry…". Because the language was then guessed from the transcript, the reply flipped to English.
3. **Grounding and fallbacks were monolingual.**
   - The doctor's and the director lines' approved text was English only, so a correct Arabic reply that mentioned any clinical term was rejected as "ungrounded".
   - The rejected reply was then replaced by the **English** approved line.
   - Interventions, discussion questions and the doctor's fallback lines had no Arabic version at all.
4. **"احكي عربي" was not understood as a command.** It went to the patient or doctor as if it were a clinical question.
5. **The voice actor could be hijacked by the content of a line.** "Of course, let's continue in English." was treated as an instruction to the voice actor: speech fidelity measured 0.00.
6. **The patient sounded formal.** He copied formal machine-translated Arabic («كنت أحمل أغراض التسوق صاعداً الدرج»).

### What changed

- **One conversation language for the whole application, shared by both characters** (`AIProviderManager.LangMode`, saved):
  - **Automatic (default).** Each reply is in the language you just spoke or typed. The conversation language follows you: director lines, interventions, discussion, debrief, subtitles, the hint line and the tablet language pack.
  - **Arabic / English (explicit).** Say «احكي عربي», «حول عربي», «خلينا نحكي بالعربي», «بدي تحكي معي عربي», «كمل بالعربي», «غير اللغة للعربي», "Speak Arabic", or the English equivalents ("speak English", «رجعنا للانكليزي»). You can also use *Pause → Language*. Both characters then stay in that language until you switch again, even if you speak the other language.
  - **Language commands are never clinical actions.** They are not scored, not recorded and not sent to the clinical engine. The character addresses replies with a short acknowledgement («تمام، بنكمّل بالعربي.»).
- **Language identification first.** The Realtime session first states which language you *spoke* (`spokenLanguage`, judged from the audio), then transcribes it in that language's script and never translates. The reply language comes from the spoken language, not from the transcript.
- **The language is guaranteed.** A reply in the wrong script is regenerated once with the language fixed. If it is still wrong, the approved line in the right language is used.
- **Bilingual grounding.** A glossary of equivalent clinical terms (ألم/وجع ↔ pain, تخطيط ↔ ECG, ضغط ↔ blood pressure, …) makes the checks language-agnostic:
  - a faithful Arabic rendering of approved English text passes;
  - a new clinical detail in either language is still blocked.
  - Arabic numbers such as «خمسة وأربعين» are read as 45.
- **Arabic approved wording for everything spoken.**
  - Interventions, discussion questions, discussion intro, the doctor's fallback lines and the debrief fallback are now in Arabic (`ER_CHEST_PAIN_001.ar.json`).
  - The patient's facts and "don't know" answers are rewritten in **Palestinian colloquial** (Ramallah) with identical clinical details. A check confirms all 18 are grounded by the English facts: same numbers, same terms.
- **Persona Arabic.**
  - The patient speaks everyday Palestinian Arabic.
  - Dr. Hassan speaks clear Levantine Arabic, uses feminine forms for herself, and keeps standard terms such as ECG in English.
- **The voice actor always reads the line as dialogue,** never as an instruction to itself.

### Optional: a stronger understanding model

Set `MEDSIM_VOICE_UNDERSTAND_MODEL=gpt-realtime-2.1` before starting the proxy. The A/B on the same Arabic clips:

| | `gpt-realtime-2.1-mini` (default) | `gpt-realtime-2.1` |
|---|---|---|
| Arabic transcription | small errors («مابخافي») | verbatim |
| Arabic wording | occasionally formal or awkward | more natural |
| Understanding time | 1.6–2.7 s | 2.3–3.1 s |
| Cost per voice turn | about $0.001 | about $0.009 |

The default stays on mini because of the 1–2 s latency goal and the test budget.

## 2. Speech cut off before the end

### Root causes

1. **Unity reads streamed audio ahead of playback.** It also pre-reads **0.8 s** when the clip is created. The old code ended a line once all audio had been *read* (plus 0.12 s), so up to about 0.8 s of the end of every line could be cut.
2. **Every streamed line started with 0.8 s of silence** (the pre-read). Measured: the real audio began at clip position 19,200. Earlier latency figures therefore understated what you heard by about 0.8 s.
3. **A cancelled line's late events ended or polluted the next line.** After an interruption, the voice actor's late `response.done` (status cancelled), and stray audio of the old line, were delivered to whatever line was current. A harmless "nothing to cancel" error also killed the next line.
4. **A whole-response timeout** could stop a long line mid-sentence, without cancelling it upstream.
5. **A new line for the same character stopped the one being spoken.** For example, a supervisor line requested earlier arriving while she answered your question.
6. **Hands-free barge-in stopped the character immediately**, even when the "speech" was noise, breathing or her own voice in the headset microphone.

### What changed

- **Playback ends on the real playhead:** the `AudioSource` position passes the clip position of the last real sample (`StreamingVoice.PlayedOut`).
- **Playback starts at the first real sample** (`StartClipPosition`). The 0.8 s of dead air is gone.
- **Voice-actor events are routed by response id.** Every response is tagged. Stale events are ignored. Errors are attributed only to the line that caused them. A cancelled line must finish upstream before the next one starts.
- **Timeouts:** first audio within 20 s, then only *inactivity* (12 s without data) fails a line. Long lines are never cut for being long.
- **The same character is never cut off by another line.** Replies and director lines wait for that character to finish. Your interruption cancels pending director lines.
- **Hands-free barge-in only lowers the character's volume.**
  - If the Realtime session finds no intelligible speech, the volume comes back and she simply continues.
  - Only real words stop her.
  - Push-to-talk (T or A/X) still interrupts at once, because pressing it is deliberate.

## 3. Arabic text under "You:"

- **Root cause:** the student transcript line set the raw string. It had no Arabic shaping and no right-to-left handling, which gave disconnected letters in reversed order. Subtitles went through `ArabicShaper.Apply`; the transcript did not.
- **Fix:**
  - The transcript now uses the same path, with an Arabic label: «أنت: …».
  - `Apply` keeps English words and numbers readable inside Arabic. TMP's right-to-left mode used to reverse them ("ECG" became "GCE"), so each left-to-right run is now pre-reversed exactly once.
  - Paired brackets are mirrored, Arabic lines are right-aligned, and English lines keep their alignment.

## 4. VR height

- **Default raised to +0.55 m** (was +0.40 m). It is saved under a new key, so the old default does not stick.
- **Pause menu row:** *Lower −5 / VR height +55 cm (press to reset) / Higher +5*. Changes apply immediately and are saved.
- Seated mode and the Comfort panel are unchanged. The headset and both controllers are raised together, so hand alignment is kept.
- **Desktop Mode height is unchanged** (+0.40 m), as covered by a PlayMode test.

## 5. VR thumbstick movement (VIVE Focus 3 via OpenXR, XRI starter rig)

| Control | Action |
|---|---|
| **Left thumbstick** | Smooth movement where you are looking: forward, back, strafe, diagonals |
| **Right thumbstick** | Snap turn 45° (default) or smooth turn; push forward and release to teleport (kept) |
| Grip | Grab instruments |
| Trigger | UI / activate |
| A / X | Push-to-talk |
| Menu | Pause |

- **Collisions:** movement goes through the rig's CharacterController (radius 0.2 m), so walls, furniture, equipment, characters and the closed door block you.
- ***Pause → Comfort, movement & graphics*** offers:
  - speed (0.8 / 1.2 / 1.6 / 2.0 m/s),
  - smooth movement on/off (teleport only),
  - snap or smooth turn,
  - snap angle or turn speed,
  - stick dead zone (0.10 / 0.20 / 0.30).
- All of these are saved.

## 6. What was verified (directly, on this PC)

| Check | How | Result |
|---|---|---|
| **Arabic, real API, 13-turn conversation** (`tools/arabic-harness.mjs`, automated audio, not a human) | Arabic from the first question with the app set to English; Palestinian follow-up; mixed Arabic + English term; reassurance; «خلينا نحكي بالعربي» → English input still answered in Arabic by both characters; Arabic ECG request (recorded as an investigation); Arabic intervention; Arabic discussion question; **92-word Arabic debrief**; "switch back to English"; Arabic input then answered in English | **13/13 correct language, 13/13 audio complete**; voicing fidelity 0.92–1.00 |
| **Arabic in the Unity client** (`AgentScripts/ArabicE2E.cs`, real API, automated audio) | Same flow through the real UI, plus the supervisor hint, hands-free false alarm and real barge-in | Auto-switch to Arabic on the first question; transcript shaped and right-to-left; command switched both characters; Arabic hint; noise over the doctor → **she kept talking**, volume restored; a real Arabic question over her → **she stopped** and the patient answered; **0 of 10 streamed lines ended before their audio** |
| **Mixed right-to-left rendering** | Screenshot of «أنت: عندك chest pain من 45 دقيقة؟ وبتاخد aspirin؟» and a doctor line with "ECG", "10" | English and numbers read correctly; Arabic joined (`Screenshots/Arabic/A12_mixed_rtl_crop.png`) |
| **Leading silence** | Play-mode measurement | Real audio started 0.8 s into every clip before the fix; a 1.0 s line now plays out in 1.00 s |
| **Unit and integration tests** | EditMode, PlayMode, proxy | EditMode **98/98**, PlayMode **21/21** (new VR locomotion and desktop-height tests), proxy **30/30** (language resolution, commands, translated-transcript case, Arabic fallbacks, stale-event routing) |
| **Real human voice** (incidental, not a planned test) | While the exe self-test ran, someone at this PC spoke into the laptop microphone | The microphone captured "Hey, how are you? Are you good?" and Arabic «سمعني مني». The doctor replied appropriately in each language (Arabic reply «حاضرة، راح أسمعك…»). |
| **Builds** | Rebuilt at the same paths | Both 0 errors; secret scans clean |

## 7. Not verified (needs you and the headset)

- The VIVE Focus 3 itself:
  - thumbstick bindings in the headset,
  - comfort of smooth movement,
  - the new height,
  - the headset microphone in Arabic,
  - lip sync seen in VR.
- A planned conversation in your own voice, in Arabic and English.
- Native-speaker review of the Palestinian Arabic, especially the doctor's phrasing, which still has occasional slips with the mini model.

## 8. Your checklist (about 15 minutes)

**Desktop (`D:\VRBuilds\MedSim_Desktop\MedSimAI.exe`, proxy running)**
1. Without changing any setting, hold **T** and ask the patient in Arabic: «من إيمتى بلّش معك الوجع؟»
   - **Expect:** an Arabic answer in his voice.
   - **Expect:** the line under the subtitle shows «أنت: …» joined and reading right-to-left.
   - **Expect:** the hint line switches to Arabic.
2. Ask a follow-up without repeating context: «وبيمتد لمحل تاني؟» (or mix in English: «عندك allergy لأي دوا؟»). **Expect:** a direct, relevant Arabic answer.
3. Say to the doctor: «خلينا نحكي بالعربي». **Expect:** «تمام، بنكمّل بالعربي.»
4. Now ask the patient something **in English**. **Expect:** he answers in Arabic (Arabic is now locked).
5. Wait for, or trigger, a supervisor hint (tablet). **Expect:** it is in Arabic.
6. Let a long answer play, for example the debrief at the end. **Expect:** every word to the end, with no sudden stop.
7. Hold T while a character is speaking. **Expect:** she stops at once (intentional interruption).
8. Press **M** for hands-free. Breathe or cough near the mic while she talks. **Expect:** her voice dips briefly and continues.
9. Say "switch back to English". **Expect:** both characters speak English again.

**VR (`D:\Medical-Test\Builds\MedSimAI_PCVR_v0.2\MedSimAI.exe`, VIVE Focus 3)**
1. Height: open the pause menu (left menu button) and use *Lower −5 / Higher +5*. Close and reopen the app. **Expect:** your height is kept.
2. Left thumbstick: walk forward, back, sideways and diagonally in the corridor and the room. **Expect:** you move where you look, and walls, the bed, the trolley, characters and the closed door block you.
3. Right thumbstick: left/right turns 45°; push forward and release to teleport.
4. *Pause → Comfort, movement & graphics:* try speed, smooth turn and dead zone.
5. Grip still grabs instruments, A/X still talks, and the menu button still pauses.
6. Repeat Desktop steps 1–3 with A/X. The headset microphone is used automatically. If not, use *Pause → Mic device*.
