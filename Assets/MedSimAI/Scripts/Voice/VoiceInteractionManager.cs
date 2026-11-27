using System;
using System.Threading;
using System.Threading.Tasks;
using MedSimAI.AI;
using MedSimAI.Characters;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace MedSimAI.Voice
{
    public enum VoiceState { Idle, Listening, Transcribing, Thinking, Speaking }

    /// <summary>
    /// Push-to-talk voice loop: mic (in memory only) → STT → ConversationOrchestrator → TTS → CharacterSpeechController.
    /// STT: proxy STT when online; Windows dictation on PC when offline; otherwise the tablet's preset questions.
    /// Raw audio is never written to disk.
    /// </summary>
    public class VoiceInteractionManager : MonoBehaviour
    {
        public static VoiceInteractionManager Instance { get; private set; }
        public CharacterSpeechController patientSpeech, instructorSpeech;
        public Transform playerHead;
        [Tooltip("Push-to-talk (XR primary button / keyboard T).")] public InputActionReference pushToTalk;
        public int maxRecordSeconds = 15;
        public Persona Target { get; private set; } = Persona.Instructor;
        public bool autoTargetByGaze = true;
        public VoiceState State { get; private set; }
        public event Action<VoiceState> StateChanged;
        public event Action<Persona> TargetChanged;
        public event Action<string> TranscriptReady;
        public event Action<ConversationTurn> TurnReady;
        public event Action<string> Error;

        InputAction pttAction; AudioClip recording; string micDevice; CancellationTokenSource cts; float listenStart;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DictationRecognizer dictation; string dictationText = "";
#endif

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
        }

        public void SetTarget(Persona p) { Target = p; TargetChanged?.Invoke(p); }

        void SetState(VoiceState s) { State = s; StateChanged?.Invoke(s); }

        void OnPttDown(InputAction.CallbackContext _) => StartListening();
        void OnPttUp(InputAction.CallbackContext _) => StopListening();

        public void StartListening()
        {
            if (State == VoiceState.Speaking || State == VoiceState.Thinking) Interrupt();
            if (State != VoiceState.Idle) return;
            listenStart = Time.time;
            var ai = AIProviderManager.Instance;
            if (ai.Mode == ConversationMode.OnlineAI && ai.OnlineStt != null && ai.OnlineStt.IsAvailable)
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
                dictation?.Dispose();
                dictationText = "";
                dictation = new DictationRecognizer(ConfidenceLevel.Low, DictationTopicConstraint.Dictation) { InitialSilenceTimeoutSeconds = maxRecordSeconds, AutoSilenceTimeoutSeconds = maxRecordSeconds };
                dictation.DictationResult += (text, _) => dictationText = (dictationText + " " + text).Trim();
                dictation.DictationHypothesis += text => TranscriptReady?.Invoke(text + "…");
                dictation.DictationError += (err, hr) => Error?.Invoke("Dictation: " + err);
                dictation.Start();
                SetState(VoiceState.Listening);
            }
            catch (Exception e) { Error?.Invoke("Offline speech recognition unavailable — use the tablet questions. " + e.Message); }
#else
            Error?.Invoke("Voice input needs the AI proxy on this device. Use the tablet questions in offline mode.");
#endif
        }

        public async void StopListening()
        {
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
                    text = await AIProviderManager.Instance.OnlineStt.TranscribeAsync(clip, AIProviderManager.Instance.Config.language, cts.Token);
                    Destroy(clip);
                }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                else if (dictation != null)
                {
                    dictation.Stop();
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (dictation.Status == SpeechSystemStatus.Running && Time.realtimeSinceStartup < until) await Task.Yield();
                    await Task.Delay(150);
                    text = dictationText;
                    dictation.Dispose(); dictation = null;
                }
#endif
            }
            catch (Exception e) { Error?.Invoke("Speech recognition failed: " + e.Message); }
            if (string.IsNullOrWhiteSpace(text)) { SetState(VoiceState.Idle); Error?.Invoke("I didn't catch that. Hold the button and try again."); return; }
            TranscriptReady?.Invoke(text);
            await Submit(Target, text);
        }

        /// <summary>Entry point for typed or preset questions as well as voice.</summary>
        public async Task Submit(Persona target, string text)
        {
            if (State == VoiceState.Speaking || State == VoiceState.Thinking) Interrupt();
            SetState(VoiceState.Thinking);
            cts?.Dispose(); cts = new CancellationTokenSource();
            ConversationTurn turn;
            try { turn = await AIProviderManager.Instance.Orchestrator.HandleAsync(target, text, cts.Token); }
            catch (OperationCanceledException) { SetState(VoiceState.Idle); return; }
            catch (Exception e) { Error?.Invoke(e.Message); SetState(VoiceState.Idle); return; }
            TurnReady?.Invoke(turn);
            await SpeakAsync(target, turn.responseText);
        }

        public async Task SpeakAsync(Persona who, string text)
        {
            var speaker = who == Persona.Patient ? patientSpeech : instructorSpeech;
            if (!speaker) { SetState(VoiceState.Idle); return; }
            AudioClip clip = null;
            var ai = AIProviderManager.Instance;
            string english = text;
            if (ai.Language != null) text = ai.Language.Localize(text); // approved translation, never runtime machine translation
            try
            {
                if (ai.Config.language == "en" || text == english) clip = await ai.OfflineTts.SynthesizeAsync(english, who, "en", CancellationToken.None);
                if (clip == null && ai.Mode == ConversationMode.OnlineAI && ai.OnlineTts != null)
                {
                    cts ??= new CancellationTokenSource();
                    clip = await ai.OnlineTts.SynthesizeAsync(text, who, ai.Config.language, cts.Token);
                }
            }
            catch (Exception e) { Debug.LogWarning("[MedSim] TTS unavailable, subtitles only: " + e.Message); }
            SetState(VoiceState.Speaking);
            void Done() { speaker.SpeechFinished -= Done; if (State == VoiceState.Speaking) SetState(VoiceState.Idle); }
            speaker.SpeechFinished += Done;
            speaker.Speak(text, clip);
        }

        public void Interrupt()
        {
            cts?.Cancel();
            AIProviderManager.Instance?.Orchestrator.CancelCurrent();
            if (patientSpeech) patientSpeech.Stop();
            if (instructorSpeech) instructorSpeech.Stop();
            SetState(VoiceState.Idle);
        }

        public void Cancel()
        {
            Interrupt();
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
