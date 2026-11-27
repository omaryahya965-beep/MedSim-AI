using System;
using System.Threading;
using System.Threading.Tasks;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace MedSimAI.Voice
{
    public enum VoiceState { Idle, Listening, Transcribing, Thinking, Speaking }

    /// <summary>
    /// Push-to-talk voice loop: mic (in memory only) → Live STT → ConversationOrchestrator (Live AI) → runtime speech
    /// → CharacterSpeechController (spatial audio + lip sync). Every character line is generated and voiced at runtime;
    /// there is NO pre-recorded dialogue. Without the AI service, characters respond with subtitles only and the status
    /// line says so. Raw microphone audio is never written to disk.
    /// </summary>
    public partial class VoiceInteractionManager : MonoBehaviour
    {
        public static VoiceInteractionManager Instance { get; private set; }
        public CharacterSpeechController patientSpeech, instructorSpeech;
        public Transform playerHead;
        [Tooltip("Push-to-talk (XR primary button / keyboard T).")] public InputActionReference pushToTalk;
        public int maxRecordSeconds = 15;
        public Persona Target { get; private set; } = Persona.Instructor;
        public bool autoTargetByGaze = true;
        /// <summary>
        /// Optional host hook (integration): called with every understood student utterance (typed, dictated or heard by the
        /// native voice session). Return true to consume it (e.g. hospital navigation handled by the guide). Null in the
        /// standalone clinical scene, so nothing changes there.
        /// </summary>
        public static Func<Persona, string, bool> UtteranceInterceptor;
        public VoiceState State { get; private set; }
        public event Action<VoiceState> StateChanged;
        public event Action<Persona> TargetChanged;
        public event Action<string> TranscriptReady;
        public event Action<ConversationTurn> TurnReady;
        public event Action<string> Error;
        /// <summary>True while any character is speaking, the student is talking, or a reply is being prepared.</summary>
        public bool Busy => State != VoiceState.Idle || (patientSpeech && patientSpeech.IsSpeaking) || (instructorSpeech && instructorSpeech.IsSpeaking) || pendingDirectorLines > 0;

        InputAction pttAction; AudioClip recording; string micDevice; CancellationTokenSource cts; float listenStart; int pendingDirectorLines;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DictationRecognizer dictation; string dictationText = "";
#endif
        AIProviderManager AI => AIProviderManager.Instance;
        public bool LiveOn => AI && AI.Mode == ConversationMode.OnlineAI && AI.Live.Configured;

        void Awake() { Instance = this; }

        void OnEnable()
        {
            pttAction = pushToTalk ? pushToTalk.action : null;
            if (pttAction == null)
            {
                pttAction = new InputAction("PushToTalk", InputActionType.Button);
                pttAction.AddBinding("<XRController>{RightHand}/primaryButton");
                pttAction.AddBinding("<XRController>{LeftHand}/primaryButton");
                pttAction.AddBinding("<Keyboard>/t");
            }
            pttAction.started += OnPttDown; pttAction.canceled += OnPttUp; pttAction.Enable();
        }

        void OnDisable() { if (pttAction != null) { pttAction.started -= OnPttDown; pttAction.canceled -= OnPttUp; } Cancel(); }

        void Update()
        {
            if (autoTargetByGaze && playerHead && State == VoiceState.Idle && patientSpeech && instructorSpeech)
            {
                float Dot(Transform t) => Vector3.Dot(playerHead.forward, (t.position - playerHead.position).normalized);
                var t2 = Dot(patientSpeech.transform) > Dot(instructorSpeech.transform) ? Persona.Patient : Persona.Instructor;
                if (t2 != Target) SetTarget(t2);
            }
            if (State == VoiceState.Listening && Time.time - listenStart > maxRecordSeconds) StopListening();
            NativeUpdate();
        }

        public void SetTarget(Persona p) { Target = p; TargetChanged?.Invoke(p); }
        void SetState(VoiceState s) { State = s; StateChanged?.Invoke(s); }
        void OnPttDown(InputAction.CallbackContext _) => StartListening();
        void OnPttUp(InputAction.CallbackContext _) => StopListening();

        public void StartListening()
        {
            if (LiveOn && NativeBeginPushToTalk()) return; // native voice: audio goes straight to the Realtime session
            if (State == VoiceState.Speaking || State == VoiceState.Thinking) Interrupt(); // the student can always interrupt
            if (State != VoiceState.Idle) return;
            listenStart = Time.time;
            if (LiveOn)
            {
                if (Microphone.devices.Length == 0) { Error?.Invoke("No microphone detected"); return; }
                micDevice = Microphone.devices[0];
                recording = Microphone.Start(micDevice, false, maxRecordSeconds, 16000);
                SetState(VoiceState.Listening);
                return;
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (PhraseRecognitionSystem.Status == SpeechSystemStatus.Running) PhraseRecognitionSystem.Shutdown();
                dictation?.Dispose(); dictationText = "";
                dictation = new DictationRecognizer(ConfidenceLevel.Low, DictationTopicConstraint.Dictation) { InitialSilenceTimeoutSeconds = maxRecordSeconds, AutoSilenceTimeoutSeconds = maxRecordSeconds };
                dictation.DictationResult += (text, _) => dictationText = (dictationText + " " + text).Trim();
                dictation.DictationHypothesis += text => TranscriptReady?.Invoke(text + "…");
                dictation.DictationError += (err, hr) => Error?.Invoke("Dictation: " + err);
                dictation.Start();
                SetState(VoiceState.Listening);
            }
            catch (Exception e) { Error?.Invoke("Speech recognition unavailable — use the tablet. " + e.Message); }
#else
            Error?.Invoke("Voice input needs the MedSim AI service. Use the tablet questions.");
#endif
        }

        public async void StopListening()
        {
            if (LiveOn && NativeEndPushToTalk()) return;
            if (State != VoiceState.Listening) return;
            SetState(VoiceState.Transcribing);
            string text = "";
            try
            {
                cts = new CancellationTokenSource();
                if (recording != null)
                {
                    int pos = Microphone.GetPosition(micDevice); Microphone.End(micDevice);
                    var clip = Trim(recording, pos); recording = null;
                    text = await AI.Live.Transcribe(clip, cts.Token); // in-memory WAV only
                    Destroy(clip);
                }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                else if (dictation != null)
                {
                    dictation.Stop();
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (dictation.Status == SpeechSystemStatus.Running && Time.realtimeSinceStartup < until) await Task.Yield();
                    await Task.Delay(150);
                    text = dictationText; dictation.Dispose(); dictation = null;
                }
#endif
            }
            catch (OperationCanceledException) { SetState(VoiceState.Idle); return; }
            catch (Exception e) { Error?.Invoke("Speech recognition failed: " + e.Message); }
            if (string.IsNullOrWhiteSpace(text)) { SetState(VoiceState.Idle); Error?.Invoke("I didn't catch that. Hold the button and try again."); return; }
            TranscriptReady?.Invoke(text);
            await Submit(Target, text);
        }

        /// <summary>Entry point for voice, typed or tablet questions.</summary>
        public async Task Submit(Persona target, string text)
        {
            var cmd = LanguageCommands.DetectCommand(text);
            if (cmd != null) // typed language switch: application-wide, not a clinical question
            {
                AI.SetLanguageMode(cmd == "ar" ? LanguageMode.Arabic : LanguageMode.English);
                await SayLive(target, "ack", LanguageCommands.Ack(target, cmd));
                return;
            }
            AI.NoteStudentLanguage(LanguageCommands.DetectLanguage(text));
            if (UtteranceInterceptor != null && UtteranceInterceptor(target, text)) { if (State == VoiceState.Thinking || State == VoiceState.Transcribing) SetState(VoiceState.Idle); return; }
            if (State == VoiceState.Speaking || State == VoiceState.Thinking) Interrupt();
            SetState(VoiceState.Thinking);
            cts?.Dispose(); cts = new CancellationTokenSource();
            ConversationTurn turn;
            try { turn = await AI.Orchestrator.HandleAsync(target, text, cts.Token); }
            catch (OperationCanceledException) { SetState(VoiceState.Idle); return; }
            catch (Exception e) { Error?.Invoke(e.Message); SetState(VoiceState.Idle); return; }
            TurnReady?.Invoke(turn);
            MedSimAI.Characters.CharacterActionRouter.Handle(turn);
            _ = SpeakAsync(target, turn.responseText, turn.audioId); // returns once tracking is done; speech continues
        }

        /// <summary>Speaks a line: runtime-generated audio when available, otherwise subtitles only (clearly labelled).</summary>
        public async Task SpeakAsync(Persona who, string text, string audioId = null)
        {
            var speaker = who == Persona.Patient ? patientSpeech : instructorSpeech;
            if (!speaker) { SetState(VoiceState.Idle); return; }
            AudioClip clip = null;
            if (!string.IsNullOrEmpty(audioId) && AI && AI.Live.Configured)
            {
                cts ??= new CancellationTokenSource();
                try { clip = await AI.Live.FetchAudio(audioId, cts.Token); }
                catch (OperationCanceledException) { SetState(VoiceState.Idle); return; }
                catch (Exception e) { Error?.Invoke("AI voice unavailable — showing text. " + e.Message); }
            }
            if (!clip && AI && !AIProviderManager.ForceOffline) Error?.Invoke(AI.Live.Configured ? "AI voice unavailable — text only" : "AI offline — text only");
            SetState(VoiceState.Speaking);
            var tcs = new TaskCompletionSource<bool>();
            void Done() { speaker.SpeechFinished -= Done; if (State == VoiceState.Speaking) SetState(VoiceState.Idle); tcs.TrySetResult(true); }
            speaker.SpeechFinished += Done;
            speaker.Speak(text, clip);
            await tcs.Task;
        }

        /// <summary>
        /// Character-initiated speech (welcome, supervisor interventions, discussion questions, results, debrief): the proxy
        /// phrases the approved line for the moment, validates it and synthesises it at runtime. Offline: approved text, subtitles only.
        /// </summary>
        public async Task<string> SayLive(Persona who, string kind, string baseText, string severity = null, DebriefPayload result = null, string fallbackText = null)
        {
            pendingDirectorLines++;
            try
            {
                string text = fallbackText ?? baseText, audio = null;
                var session = ClinicalScenarioManager.Instance.Session;
                if (LiveOn)
                {
                    var streamed = await NativeSayStream(who, kind, baseText, severity, result);
                    if (streamed != null) return streamed;
                }
                if (LiveOn && false)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(AI.Live.SessionId)) await AI.Live.StartSession(session.Scenario);
                        var r = await AI.Live.Say(who, kind, baseText, LiveAIClient.ContextOf(session, LiveProvider.DiscussionId), CancellationToken.None, severity, result);
                        if (r != null && !string.IsNullOrEmpty(r.text)) { text = r.text; audio = r.audioId; }
                    }
                    catch (Exception e) { Error?.Invoke(e.Message); }
                }
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (AI) text = AI.Language != null ? AI.Language.Localize(text) : text;
                while (State == VoiceState.Listening || State == VoiceState.Transcribing) await Task.Yield(); // never talk over the student
                await SpeakAsync(who, text, audio);
                return text;
            }
            finally { pendingDirectorLines--; }
        }

        public void Interrupt()
        {
            NativeAbort();
            cts?.Cancel();
            AIProviderManager.Instance?.Orchestrator.CancelCurrent();
            if (patientSpeech) patientSpeech.Stop();
            if (instructorSpeech) instructorSpeech.Stop();
            SetState(VoiceState.Idle);
        }

        public void Cancel()
        {
            Interrupt();
            NativeCloseMic();
            if (recording != null) { Microphone.End(micDevice); recording = null; }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            dictation?.Dispose(); dictation = null;
#endif
        }

        static AudioClip Trim(AudioClip src, int samples)
        {
            samples = Mathf.Clamp(samples, 1, src.samples);
            var data = new float[samples * src.channels]; src.GetData(data, 0);
            var c = AudioClip.Create("utterance", samples, src.channels, src.frequency, false); c.SetData(data, 0);
            Destroy(src);
            return c;
        }
    }
}
