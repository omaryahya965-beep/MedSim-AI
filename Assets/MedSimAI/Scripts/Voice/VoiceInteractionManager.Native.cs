using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace MedSimAI.Voice
{
    public enum MicMode { PushToTalk = 0, HandsFree = 1, Muted = 2 }

    /// <summary>
    /// Native voice (v0.3): the microphone stays open (in memory only); each student utterance — captured by push-to-talk or
    /// hands-free voice activity detection — is sent as audio straight to the proxy's persistent Realtime session
    /// (no separate speech-to-text request). The validated reply streams back as speech and starts playing immediately.
    /// The student can interrupt a character at any time (push-to-talk, or speaking clearly over it in hands-free mode).
    /// Transcripts/subtitles keep working: the Realtime session reports what it heard, and the reply text is validated.
    /// </summary>
    public partial class VoiceInteractionManager
    {
        public const string MicModePref = "medsim.micMode";
        const int Rate = StreamingVoice.Rate, Frame = 480; // 20 ms frames at 24 kHz
        MicMode nvMic;
        public MicMode Mic { get { if (!nvInit) { nvInit = true; nvMic = (MicMode)PlayerPrefs.GetInt(MicModePref, (int)MicMode.PushToTalk); } return nvMic; } private set { nvInit = true; nvMic = value; } }
        float nvPttReleased = -100f;
        public event Action<MicMode> MicModeChanged;
        /// <summary>End of student speech → reply understood (ms) and → first audio received (ms), last voice turn.</summary>
        public float LastUnderstoodMs { get; private set; } = -1;
        public float LastFirstAudioMs { get; private set; } = -1;
        public float MicLevel { get; private set; }
        /// <summary>Automation hook: utterances captured from a WAV file instead of the microphone (same pipeline afterwards).</summary>
        public Action<float[]> UtteranceCaptured;

        string nvDevice; AudioClip nvClip; int nvLastPos; bool nvCapturing, nvPtt, nvInit; float nvNoise = 0.004f, nvSilence, nvHoldoffUntil; int nvSpeechFrames;
        readonly List<float> nvCapture = new List<float>(Rate * 16);
        readonly Queue<float[]> nvPreroll = new Queue<float[]>();
        float[] nvRead = new float[Rate]; readonly float[] nvFrame = new float[Frame]; int nvFill;
        UnityWebRequest nvReq;
        readonly HashSet<UnityWebRequest> nvSayReqs = new HashSet<UnityWebRequest>();
        bool nvBargeIn; int nvEpoch; // epoch: incremented by every interruption (stale director lines are dropped)
        float nvEcho = 0.6f; readonly float[] nvOut = new float[512]; // estimated speaker→microphone echo gain (laptop speakers)

        bool AnyCharacterSpeaking => (patientSpeech && patientSpeech.IsSpeaking) || (instructorSpeech && instructorSpeech.IsSpeaking);

        public void SetMicMode(MicMode m)
        {
            Mic = m; PlayerPrefs.SetInt(MicModePref, (int)m); PlayerPrefs.Save();
            if (m == MicMode.Muted) { if (nvCapturing) { nvCapturing = false; SetState(VoiceState.Idle); } NativeCloseMic(); }
            MicModeChanged?.Invoke(m);
        }
        public MicMode CycleMicMode() { SetMicMode((MicMode)(((int)Mic + 1) % 3)); return Mic; }
        /// <summary>Sets the mode only if the student has never chosen one.</summary>
        public void SetDefaultMicMode(MicMode m) { if (!PlayerPrefs.HasKey(MicModePref)) SetMicMode(m); }
        public const string MicDevicePref = "medsim.micDevice";
        /// <summary>Microphone in use (null = the operating system's default device).</summary>
        public string MicDevice => nvDevice;
        float nvCapPeak;

        /// <summary>
        /// Picks the microphone: a saved choice if present; in VR the headset's microphone (e.g. "VIVE Virtual Audio Device");
        /// otherwise the operating system's default input (Unity's device list order is not the system default).
        /// </summary>
        public static string PickMicDevice()
        {
            var devs = Microphone.devices; if (devs.Length == 0) return null;
            var saved = PlayerPrefs.GetString(MicDevicePref, "");
            if (!string.IsNullOrEmpty(saved) && System.Array.IndexOf(devs, saved) >= 0) return saved;
            if (MedSimAI.XR.DisplayModeManager.Mode == MedSimAI.XR.DisplayMode.VR)
                foreach (var d in devs) { var l = d.ToLowerInvariant(); if (l.Contains("vive") || l.Contains("headset") || l.Contains("oculus") || l.Contains("quest") || l.Contains("index") || l.Contains("steam")) return d; }
            return null;
        }
        public void ReopenMic() { NativeCloseMic(); }

        /// <summary>Hands-free keeps the microphone open (in memory only); push-to-talk opens it only while the talk button is held.</summary>
        void NativeEnsureMic(bool onDemand = false)
        {
            if (nvClip || Mic == MicMode.Muted || Microphone.devices.Length == 0) return;
            if (Mic == MicMode.PushToTalk && !onDemand) return;
            nvDevice = PickMicDevice();
            nvClip = Microphone.Start(nvDevice, true, 20, Rate);
            Debug.Log($"[MedSim] Microphone open (in memory only): {(nvDevice ?? "system default")} · mode {Mic}");
            nvLastPos = 0; nvFill = 0; nvPreroll.Clear();
        }

        public void NativeCloseMic() { if (nvClip) { Microphone.End(nvDevice); nvClip = null; } }

        void NativeUpdate()
        {
            if (!LiveOn) return;
            NativeEnsureMic();
            if (!nvClip) return;
            if (Mic == MicMode.PushToTalk && !nvPtt && !nvCapturing && Time.time - nvPttReleased > 3f) { NativeCloseMic(); return; } // release the microphone between push-to-talk turns
            int pos = Microphone.GetPosition(nvDevice); if (pos < 0) return;
            int n = pos - nvLastPos; if (n < 0) n += nvClip.samples; if (n <= 0) return;
            if (nvRead.Length < n) nvRead = new float[n];
            var tmp = n == nvRead.Length ? nvRead : new float[n];
            nvClip.GetData(tmp, nvLastPos); // wraps around the looping buffer
            nvLastPos = pos;
            for (int i = 0; i < n; i++) { nvFrame[nvFill++] = tmp[i]; if (nvFill == Frame) { ProcessFrame(); nvFill = 0; } }
        }

        void ProcessFrame()
        {
            float sum = 0; for (int i = 0; i < Frame; i++) sum += nvFrame[i] * nvFrame[i];
            float rms = Mathf.Sqrt(sum / Frame); MicLevel = rms;
            var copy = (float[])nvFrame.Clone();
            if (nvCapturing)
            {
                nvCapture.AddRange(copy);
                if (!nvPtt)
                {
                    nvCapPeak = Mathf.Max(nvCapPeak, rms);
                    nvSilence = rms < Mathf.Max(Mathf.Max(0.006f, nvNoise * 2.5f), nvCapPeak * 0.15f) ? nvSilence + 0.02f : 0f; // relative to this utterance's level
                    if (nvSilence >= 0.65f) NativeEndCapture();
                }
                if (nvCapture.Count > Rate * maxRecordSeconds) NativeEndCapture();
                return;
            }
            nvPreroll.Enqueue(copy); while (nvPreroll.Count > 15) nvPreroll.Dequeue(); // 300 ms pre-roll
            bool speaking = AnyCharacterSpeaking, busyChar = speaking || Time.time < nvHoldoffUntil;
            float outRms = 0f;
            if (speaking)
            {
                AudioListener.GetOutputData(nvOut, 0); float os = 0; for (int i = 0; i < nvOut.Length; i++) os += nvOut[i] * nvOut[i];
                outRms = Mathf.Sqrt(os / nvOut.Length);
                if (outRms > 0.01f) { float ratio = rms / outRms; if (ratio < nvEcho * 2.5f) nvEcho = Mathf.Clamp(Mathf.Lerp(nvEcho, ratio, 0.04f), 0.02f, 3f); } // learn the echo level while a character talks
            }
            if (!busyChar) nvNoise = Mathf.Lerp(nvNoise, Mathf.Max(rms, 0.0008f), rms < nvNoise ? 0.05f : (nvSpeechFrames > 0 ? 0f : 0.004f)); // noise floor: falls fast, rises slowly with steady background noise
            if (Mic != MicMode.HandsFree || State == VoiceState.Thinking || State == VoiceState.Transcribing) { nvSpeechFrames = 0; return; }
            float thresh = Mathf.Max(0.015f, nvNoise * 4f);
            if (busyChar) thresh = Mathf.Max(Mathf.Max(0.06f, thresh * 3.5f), outRms * nvEcho * 3f); // barge-in must be clearly louder than the characters' echo
            nvSpeechFrames = rms > thresh ? nvSpeechFrames + 1 : 0;
            if (nvSpeechFrames >= (busyChar ? 10 : 6)) NativeBeginCapture(); // 200 ms over a character, 120 ms otherwise
        }

        void NativeBeginCapture()
        {
            // Push-to-talk is deliberate: interrupt at once. Hands-free: only DUCK the character until the Realtime session
            // confirms real words (noise, breathing or the character's own voice in the mic must never cut speech off).
            if (nvPtt) { if (AnyCharacterSpeaking || nvReq != null) Interrupt(); }
            else if (AnyCharacterSpeaking) { nvBargeIn = true; DuckSpeech(true); }
            nvCapturing = true; nvSilence = 0; nvSpeechFrames = 0; nvCapPeak = 0;
            nvCapture.Clear(); foreach (var f in nvPreroll) nvCapture.AddRange(f); nvPreroll.Clear();
            listenStart = Time.time;
            SetState(VoiceState.Listening);
        }

        void NativeEndCapture()
        {
            nvCapturing = false; nvPtt = false;
            int trim = (int)(Mathf.Clamp(nvSilence - 0.15f, 0f, 0.6f) * Rate);
            int len = Mathf.Max(0, nvCapture.Count - trim);
            if (len < Rate * 0.35f) { if (nvBargeIn) { nvBargeIn = false; DuckSpeech(false); } SetState(VoiceState.Idle); return; } // too short: ignore clicks/coughs
            var samples = nvCapture.GetRange(0, len).ToArray();
            UtteranceCaptured?.Invoke(samples);
            SubmitVoice(Target, samples);
        }

        bool NativeBeginPushToTalk()
        {
            if (Mic == MicMode.Muted) { Error?.Invoke("Microphone is muted — unmute it in the pause menu (or press M)"); return true; }
            NativeEnsureMic(true);
            if (!nvClip) { Error?.Invoke("No microphone detected"); return true; }
            nvPtt = true;
            if (!nvCapturing) NativeBeginCapture();
            return true;
        }

        bool NativeEndPushToTalk()
        {
            nvPttReleased = Time.time;
            if (!nvPtt && !nvCapturing) return true;
            nvPtt = false;
            if (nvCapturing) NativeEndCapture();
            return true;
        }

        /// <summary>Sends one utterance (float samples at 24 kHz) through the native voice path. Public for automation.</summary>
        public void SubmitVoice(Persona target, float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++) { short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f); bytes[2 * i] = (byte)v; bytes[2 * i + 1] = (byte)(v >> 8); }
            bool barge = nvBargeIn; nvBargeIn = false;
            StartCoroutine(VoiceRoutine(target, bytes, Time.realtimeSinceStartup, barge));
        }

        void DuckSpeech(bool on) { if (patientSpeech) patientSpeech.Duck(on); if (instructorSpeech) instructorSpeech.Duck(on); }

        IEnumerator VoiceRoutine(Persona target, byte[] pcm, float tEnd, bool bargeIn = false)
        {
            var prevReq = nvReq;
            SetState(VoiceState.Thinking);
            LastUnderstoodMs = LastFirstAudioMs = -1;
            var session = ClinicalScenarioManager.Instance.Session;
            if (string.IsNullOrEmpty(AI.Live.SessionId)) { var t = AI.Live.StartSession(session.Scenario); while (!t.IsCompleted) yield return null; }
            var stream = new StreamingVoice("live_voice");
            LiveTurnResponse meta = null;
            var www = AI.Live.VoiceTurn(target, pcm, LiveAIClient.ContextOf(session, LiveProvider.DiscussionId), stream, m => meta = m);
            nvReq = www;
            var op = www.SendWebRequest();
            while (meta == null && !op.isDone) yield return null;
            if (meta != null && meta.noSpeech) // the Realtime session heard no intelligible student speech (noise, echo): stay silent
            {
                if (nvReq == www) nvReq = prevReq != null && !prevReq.isDone ? prevReq : null;
                www.Dispose(); AI.Live.ReportOnline();
                if (bargeIn) DuckSpeech(false); // false alarm: the character simply continues
                Debug.Log("[MedSim] Native voice: no intelligible speech in the captured audio, ignored");
                if (State == VoiceState.Thinking) SetState(VoiceState.Idle); yield break;
            }
            if (meta == null || string.IsNullOrEmpty(meta.text))
            {
                if (nvReq == www) { AI.Live.ReportError(www.responseCode, www.downloadHandler?.text); Error?.Invoke(AI.Live.LastError ?? "AI voice unavailable"); }
                nvReq = null; www.Dispose(); if (State == VoiceState.Thinking) SetState(VoiceState.Idle); yield break;
            }
            AI.Live.ReportOnline();
            LastUnderstoodMs = (Time.realtimeSinceStartup - tEnd) * 1000f;
            if (bargeIn) // confirmed: the student really spoke over the character → now interrupt
            {
                if (patientSpeech) patientSpeech.Stop(); if (instructorSpeech) instructorSpeech.Stop(); DuckSpeech(false);
                if (prevReq != null && prevReq != www) { try { prevReq.Abort(); } catch { } }
                AbortSayRequests(); nvEpoch++;
            }
            TranscriptReady?.Invoke(meta.heard);
            var speaker = target == Persona.Patient ? patientSpeech : instructorSpeech;
            if (!string.IsNullOrEmpty(meta.languageCommand))
            {
                // Language switch ("احكي عربي", "speak English"…): application-wide, both characters; never scored.
                AI.SetLanguageMode(meta.languageCommand == "ar" ? LanguageMode.Arabic : LanguageMode.English);
                Debug.Log("[MedSim] Language command → " + meta.languageCommand + " (both characters)");
                bool fin = false; void FinCmd() { speaker.SpeechFinished -= FinCmd; fin = true; }
                speaker.SpeechFinished += FinCmd; SetState(VoiceState.Speaking);
                speaker.SpeakStreaming(meta.text, stream);
                while (!fin) yield return null;
                if (nvReq == www) nvReq = null; www.Dispose();
                if (State == VoiceState.Speaking) SetState(VoiceState.Idle);
                yield break;
            }
            AI.NoteStudentLanguage(LanguageCommands.DetectLanguage(meta.heard) ?? meta.lang);
            var resp = new AIResponse
            {
                live = true, providerName = "Live AI (native voice)", speech = meta.text, usedFactIds = meta.factIds ?? new string[0], intent = meta.intent,
                investigationId = string.IsNullOrEmpty(meta.investigationId) ? null : meta.investigationId, escalationRequested = meta.escalationRequested,
                diagnosisNamed = meta.diagnosisNamed, isQuestion = meta.isQuestion, concepts = meta.concepts ?? new string[0], liveSource = meta.source,
                action = meta.action, actions = meta.actions, supervisorIntervention = meta.supervisorIntervention
            };
            var turn = AI.Orchestrator.ApplyVoiceTurn(target, meta.heard, resp);
            TurnReady?.Invoke(turn);
            // Never cut the same character off mid-line: if they are still finishing another line, wait for it.
            float busyUntil = Time.realtimeSinceStartup + 25f;
            while (speaker.IsSpeaking && Time.realtimeSinceStartup < busyUntil) yield return null;
            bool done = false; void Done() { speaker.SpeechFinished -= Done; done = true; }
            speaker.SpeechFinished += Done;
            SetState(VoiceState.Speaking);
            if (turn.responseText != meta.text) { stream.Cancel(); speaker.Speak(turn.responseText); } // client safety guard replaced the reply: never voice the original
            else speaker.SpeakStreaming(turn.responseText, stream);
            MedSimAI.Characters.CharacterActionRouter.Handle(turn); // the body moves while the acknowledgement is spoken
            while (stream.FirstAudioTime < 0 && !stream.Drained && !done) yield return null;
            if (stream.FirstAudioTime > 0) LastFirstAudioMs = (stream.FirstAudioTime - tEnd) * 1000f;
            Debug.Log($"[MedSim] Native voice turn ({target}): understood {LastUnderstoodMs:0} ms, first audio {LastFirstAudioMs:0} ms after end of speech · source {meta.source}{(string.IsNullOrEmpty(meta.rejected) ? "" : " · reply rejected by grounding (" + meta.rejected + ")")}");
            while (!done) yield return null;
            if (nvReq == www) nvReq = null;
            www.Dispose();
            nvHoldoffUntil = Time.time + 0.35f;
            if (State == VoiceState.Speaking) SetState(VoiceState.Idle);
        }

        /// <summary>Character-initiated line with streamed speech. Returns null if the stream could not start (caller falls back).</summary>
        async Task<string> NativeSayStream(Persona who, string kind, string baseText, string severity, DebriefPayload result)
        {
            var session = ClinicalScenarioManager.Instance.Session;
            if (string.IsNullOrEmpty(AI.Live.SessionId)) await AI.Live.StartSession(session.Scenario);
            var stream = new StreamingVoice("live_say");
            LiveSayResponse meta = null;
            var www = AI.Live.SayStream(who, kind, baseText, LiveAIClient.ContextOf(session, LiveProvider.DiscussionId), stream, m => meta = m, severity, result);
            int epoch = nvEpoch; nvSayReqs.Add(www);
            var op = www.SendWebRequest();
            while (meta == null && !op.isDone) await Task.Yield();
            if (epoch != nvEpoch) { nvSayReqs.Remove(www); www.Dispose(); return meta?.text ?? ""; } // interrupted meanwhile: drop the line
            if (meta == null || string.IsNullOrEmpty(meta.text)) { nvSayReqs.Remove(www); AI.Live.ReportError(www.responseCode, www.downloadHandler?.text); www.Dispose(); return null; }
            AI.Live.ReportOnline();
            var speaker = who == Persona.Patient ? patientSpeech : instructorSpeech;
            float waitUntil = Time.realtimeSinceStartup + 30f;
            // don't talk over the student, and never cut this character off mid-line: wait (bounded) until both are free
            while ((State == VoiceState.Listening || State == VoiceState.Transcribing || State == VoiceState.Thinking || nvCapturing || speaker.IsSpeaking) && Time.realtimeSinceStartup < waitUntil && epoch == nvEpoch) await Task.Yield();
            if (epoch != nvEpoch) { stream.Cancel(); nvSayReqs.Remove(www); www.Dispose(); return meta.text; }
            var tcs = new TaskCompletionSource<bool>();
            void Done() { speaker.SpeechFinished -= Done; if (State == VoiceState.Speaking) SetState(VoiceState.Idle); tcs.TrySetResult(true); }
            speaker.SpeechFinished += Done;
            SetState(VoiceState.Speaking);
            speaker.SpeakStreaming(meta.text, stream);
            await tcs.Task;
            nvSayReqs.Remove(www); www.Dispose();
            return meta.text;
        }

        void NativeAbort()
        {
            nvEpoch++;
            if (nvReq != null) { try { nvReq.Abort(); } catch { } nvReq = null; }
            AbortSayRequests(); DuckSpeech(false); nvBargeIn = false;
        }
        void AbortSayRequests() { foreach (var r in nvSayReqs) { try { r.Abort(); } catch { } } nvSayReqs.Clear(); }
    }
}
