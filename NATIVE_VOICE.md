# Native voice (v0.3): Realtime speech-to-speech

The student talks to Dr. Hassan and Mr. Reyes by voice. The student's audio goes straight to a persistent OpenAI Realtime session through the local MedSim proxy, so there is no separate speech-to-text request. The reply is checked against the scenario before it is spoken, and it then streams back as speech while it is still being generated.

Text features are unchanged:
- subtitles,
- the "You: …" transcript line,
- typed and tablet questions,
- interaction prompts,
- the debrief.

## How a voice turn works

```
mic (in memory) ──push-to-talk or hands-free VAD──► PCM16 24 kHz utterance
   │  POST /v1/voice  (127.0.0.1 proxy; the API key never leaves the proxy)
   ▼
Realtime understanding session (persistent per persona, pre-warmed at session start)
   • hears the audio → returns {heard, act/intent, fact ids, concepts, escalation, …, proposed reply}
     via a forced function call (structured, no free speech)
   ▼
Deterministic grounding (unchanged rules): numbers, clinical terms, diagnosis words, dosing, meta talk
   checked against the approved scenario facts for the current stage → reply kept, or replaced by an approved line
   ▼
JSON header line → Unity: transcript ("You: …"), subtitles, deterministic tracking of what was heard
   ▼
Realtime voice actor (persistent, pooled) speaks the validated text → PCM streamed → plays immediately
```

- **The scenario stays authoritative.**
  - The Realtime model proposes a reply, but only text that passes grounding is spoken.
  - The deterministic engine processes the words the session heard: facts, investigations, escalation, reasoning concepts and assessment.
  - In tests, the doctor's "Do you think this could be a STEMI?" answer was blocked (`diagnosis_disclosure`), and the approved line "What do you think it is, and what supports that?" was spoken instead.
- **Noise and echo produce no reply.** If the session hears no intelligible student speech (silence, a cough, or the characters' own voices picked up by the microphone), the turn is dropped silently. Nothing is spoken, recorded or added to history.
- **Character-initiated lines** (welcome, supervisor interventions, discussion questions, debrief) use the same streamed speech path through `POST /v1/say-stream`.
- **Fallbacks:**
  - If streaming fails, the previous path (`/v1/say` and the audio fetch) is used.
  - If the AI is offline, approved text is shown as subtitles, clearly labelled.
- **Client-side safety:** if a reply contains dosing content, the client replaces it and does **not** play the streamed audio. The approved line is shown instead.

## Microphone modes

| Mode | How | Default |
|---|---|---|
| **Push-to-talk** | Hold **T** (desktop) or **A/X** (VR). The microphone opens on press and closes 3 s after release. | Yes (both modes) |
| **Hands-free** | Just speak. Voice activity detection uses an adaptive noise floor, ends the turn after 0.65 s of silence (measured against your own speech level), and ignores captures shorter than 0.35 s. The microphone stays open, in memory only. | — |
| **Muted** | No microphone. Typed and tablet questions still work. | — |

- **Switching mode:** press **M** (desktop) or use the pause menu: *Microphone: …* and *Mic device: …*.
- **Microphone device:**
  - VR uses the headset microphone (for example "VIVE Virtual Audio Device").
  - Desktop uses the Windows default input. Unity's first-listed device is not necessarily the Windows default; on this laptop it was the far-field mic array.
- **Interruptions:**
  - Pressing push-to-talk while a character speaks stops the speech at once and cancels the request. The proxy sends `response.cancel`.
  - In hands-free mode, speaking clearly over a character also interrupts it. This barge-in is echo-gated: the threshold follows the speaker output level, so the characters' voices coming out of laptop speakers do not trigger it.
- **Privacy:** audio is held in memory only, sent only to the local proxy, and never written to disk.

## Measured latency (end of student speech → first reply audio)

> **v0.4 correction.** Unity pre-reads 0.8 s of silence when a streaming clip is created, so every streamed line used to start with 0.8 s of dead air. The figures below measured when audio *arrived*, so what you actually *heard* was about 0.8 s later. v0.4 starts playback at the first real sample. Measured again in the Unity client (Arabic and English, v0.4): first audio **2.1–3.0 s** after the end of speech, and that is now what you hear. See ARABIC_AUDIO_VR.md.

These figures come from the Unity client with the real proxy and API. The utterances are synthetic, **automated audio** clips, not a human voice. VAD time is not included except where noted.

| Turn | Understood | First audio |
|---|---|---|
| EN, patient, introduction and onset question | 2.16 s | 2.88 s |
| EN, patient, allergy | 0.97 s | 1.57 s |
| EN, doctor, ECG request | 2.06 s | 2.76 s |
| EN, doctor, "I think he is having a heart attack" | 1.65 s | 2.17 s |
| EN, doctor, STEMI question (approved fallback) | 1.09 s | 1.69 s |
| EN, doctor, escalation | 1.78 s | 2.38 s |
| AR (Palestinian), patient, onset | 1.23 s | 1.97 s |
| EN, patient, hands-free soak | 1.11 s | 1.69 s |
| EN, doctor, hands-free soak | 1.31 s | 1.83 s |
| **Standalone exe**, EN patient allergy | 1.08 s | 1.88 s |
| **Standalone exe, acoustic loopback through the real mic** | reply started 1.55 s after the clip ended (includes the 0.65 s VAD end-of-speech window) | — |

- **Summary:** median first audio about **1.9 s** (range 1.57–2.88 s); 6 of 10 turns were at or under 2.0 s.
- **Previous pipeline:** speech-to-text → chat model → text-to-speech took **4.5–5.5 s**.
- **Slower turns:** the 2.8–2.9 s turns are the longest, multi-clause replies. Understanding time grows with how much the model writes.
- **Interruption:** the character stopped within one frame (0–1 ms after the interrupt). The proxy logs the cancelled stream as `(interrupted)`.

## Cost

- **Per voice turn:** about $0.001 for understanding plus about $0.0013–0.0015 for speech, roughly the same as the old pipeline.
- **This phase:** about $0.33 of API usage in total, including a one-off $0.15 spent generating lip-sync calibration vowels.
- **Ledger total:** $0.84 of $2.00, so **$1.16 remains**. The budget was not reset or increased.

## Lip sync

- uLipSync now uses project profiles calibrated on the runtime voices:
  - `Characters/v2/LipSync/Patient_cedar.asset`
  - `Characters/v2/LipSync/Doctor_marin.asset`
- **Vowels:** the A, I, U, E and O phonemes were calibrated from sustained vowels in each voice, using the steadiest 1.4 s window (26 frames each). Silence and S keep the sample data.
- **Loudness:** each character's voiced loudness range was measured on real reply sentences:

  | Character | Typical (median) | Loud (97th percentile) | Range now used |
  |---|---|---|---|
  | Patient (`cedar`) | −2.25 | −1.78 | −2.90 to −1.78 |
  | Doctor (`marin`) | −1.84 | −1.35 | −2.49 to −1.35 |

  Values are log₁₀ volume. The default −2.5 to −1.5 range barely opened the patient's mouth.
- **Streamed speech** plays through the same AudioSource, so uLipSync drives the mouth for it as well.

## Microphone verification checklist (for you, with your own voice)

Automated tests used synthetic voices. One of them was played acoustically through the laptop speakers into the real laptop microphone. **No human voice has been tested.** Please check the following:

1. Start the proxy (`Backend/medsim-proxy`: `node server.mjs`), then run `D:\VRBuilds\MedSim_Desktop\MedSimAI.exe`.
2. The top-left status should show **LIVE AI** and **Mic: push-to-talk (T)**.
3. In the corridor, look at Dr. Hassan, hold **T**, say "Hello Doctor Hassan, can I see the patient?", then release.
   - **Expect:** the status shows "Listening…" while you hold T; Windows shows the microphone-in-use icon only while you hold T; "You: …" shows your words; she answers by voice within about 2 s, with subtitles.
4. Ask the patient something in Arabic (switch language in the pause menu): «من إيمتى بلش الوجع؟»
   - **Expect:** an Arabic transcript and an Arabic spoken reply.
5. Interrupt: while the doctor is talking, hold T and start speaking. She should stop at once.
6. Press **M** to switch to hands-free and speak without pressing anything.
   - **Expect:** the turn ends about 0.7 s after you stop talking.
   - Stay silent for 30 s while a character talks through the laptop speakers. **Expect: no "You: …" line.** If you get stray turns, use headphones or go back to push-to-talk.
7. If your words come out wrong, open *Pause → Mic device* and pick your microphone. If nothing is heard, check *Windows Settings → Privacy → Microphone → Let desktop apps access your microphone*.
8. VR: hold **A/X**. The VIVE headset microphone is selected automatically. Repeat steps 3–5 **(not yet tested on the headset)**.

## Files

- **Proxy:**
  - `lib/openai.mjs`: the `RealtimeUnderstanding` session, plus streaming `speak()` with cancel.
  - `lib/live.mjs`: `voiceTurn`, `sayStream`, `cleanReply`, `isNoSpeech`.
  - `server.mjs`: the `/v1/voice` and `/v1/say-stream` routes, and the latency/source/rejection log line.
- **Unity:**
  - `Voice/VoiceInteractionManager.Native.cs`: microphone, VAD, modes, interruption, streaming turns.
  - `Voice/StreamingVoice.cs`: ring-buffered streaming AudioClip and framed-response parser.
  - `AI/LiveAIClient.cs` (`VoiceTurn`, `SayStream`), `AI/ConversationOrchestrator.ApplyVoiceTurn`, `Characters/CharacterSpeechController.SpeakStreaming`.
- **Tests:**
  - EditMode `NativeVoiceTests` (6).
  - Proxy `live.test.mjs` (no-speech, reply clean-up).
  - `AgentScripts/NativeVoiceE2E.cs` (live, in Unity).
  - `Core/SelfTestRunner.cs`: exe self-test, `MedSimAI.exe -medsimSelfTest <student-audio folder>`.
  - `tools/doctor-replay.mjs`, `tools/voice-harness.mjs`.
