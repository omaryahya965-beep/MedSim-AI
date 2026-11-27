using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Characters;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Characters;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Mocks;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.UI;
using MedSimAI.Voice;
using UnityEngine;
using UnityEngine.Networking;

namespace MedSimAI.Surgery.Integration
{
    /// <summary>
    /// Runs one surgical training session in the operating-room scene. Owns the engine-free SurgerySessionCore (bus,
    /// supervisor, assessment, hand-over) and connects it to: Person 3's mechanics (ISurgicalScenarioRuntime /
    /// IInstrumentRegistry found in the scene, or the clearly labelled mock), the surgeon and assistant characters, the
    /// student's voice (native Realtime via the existing proxy), and the HUD. An AI failure never touches simulation
    /// state: the session keeps running text-only with approved lines.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SurgerySessionController : MonoBehaviour
    {
        public string scenarioId = SurgeryContract.SCENARIO_ID_TKA;
        public SurgeryHud hud;
        public SurgeryCharacterActuator actuator;
        public SurgeryVoiceInput voice;
        [Tooltip("Use the mock mechanics when no ISurgicalScenarioRuntime is in the scene (test scenes / before feature/surgery-mechanics is merged).")]
        public bool allowMockMechanics = true;
        public float tickSeconds = 0.25f;

        public SurgerySessionCore Core { get; private set; }
        public SurgeryLiveClient Live { get; private set; }
        public bool UsingMockMechanics { get; private set; }
        public SurgicalScenarioData Scenario { get; private set; }

        SurgeryEventBus bus; float nextTick; UnityWebRequest currentReq; StreamingVoice currentStream; CharacterSpeechController currentSpeaker;
        readonly Queue<CharacterLine> queue = new Queue<CharacterLine>(); bool speaking, voiceTurnBusy;
        public static SurgerySessionController Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            var ta = Resources.Load<TextAsset>("SurgeryScenarios/" + scenarioId);
            if (!ta) { Debug.LogError("[Surgery] Scenario not found: " + scenarioId); enabled = false; return; }
            Scenario = JsonUtility.FromJson<SurgicalScenarioData>(ta.text);
            var host = SurgeryEventBusHost.Instance ? SurgeryEventBusHost.Instance : new GameObject("[Surgery Event Bus]").AddComponent<SurgeryEventBusHost>();
            bus = host.Bus;
            Core = new SurgerySessionCore(Scenario, () => Time.timeAsDouble, bus) { Language = ScenarioRouter.Language };
            var derived = EnvironmentAnchorAdapter.EnsureDerived();
            if (derived.Length > 0) Debug.Log("[Surgery] Anchors " + derived.Trim().Replace("\n", ", "));
        }

        IEnumerator Start()
        {
            if (!enabled) yield break;
            // Mechanics (Person 3) or the mock.
            var runtime = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<ISurgicalScenarioRuntime>().FirstOrDefault();
            var registry = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IInstrumentRegistry>().FirstOrDefault();
            if (runtime == null && allowMockMechanics)
            {
                UsingMockMechanics = true;
                runtime = new MockScenarioRuntime(Scenario.scenarioId, Scenario.Ordered.Select(o => o.id));
                Core.AcceptMockAsMechanics = true;
                Debug.LogWarning("[Surgery] No mechanics in scene: using MOCK mechanics (keyboard driver). Results are labelled as mock.");
            }
            Core.Runtime = runtime;
            Core.Instruments = registry ?? new SceneInstrumentRegistry(actuator);
            Core.Actuator = actuator;
            if (!hud) hud = FindAnyObjectByType<SurgeryHud>();
            if (!voice) voice = FindAnyObjectByType<SurgeryVoiceInput>();
            var head = Camera.main ? Camera.main.transform : null;
            if (actuator) { if (actuator.surgeon) actuator.surgeon.student = head; if (actuator.assistant) actuator.assistant.student = head; }

            Core.Line += l => { queue.Enqueue(l); };
            Core.ActionFinished += (v, r) => Live?.PostActionResult(r, r.outcome.ToString().ToLowerInvariant());
            Core.Handoff.Completed += t => { actuator?.HandOverCompleted(); Live?.PostActionResult(new CharacterActionResult { characterId = SurgeryCharacterIds.Assistant, action = SurgeryActionCatalog.HandInstrument, instrumentId = t }, "handoff_completed"); };
            Core.Handoff.Failed += t => { actuator?.HandOverFailed(t); Live?.PostActionResult(new CharacterActionResult { characterId = SurgeryCharacterIds.Assistant, action = SurgeryActionCatalog.HandInstrument, instrumentId = t }, "handoff_not_taken"); };
            Core.SessionCompleted += OnCompleted;
            bus.EventPublished += OnBusEvent;
            if (voice) { voice.UtteranceReady += OnUtterance; voice.StartedSpeaking += InterruptSpeech; voice.ModeChanged += m => RefreshMic(); }
            if (hud)
            {
                hud.Language = Core.Language;
                hud.RestartRequested += Restart; hud.MenuRequested += ReturnToMenu; hud.FinishRequested += () => Core.Complete();
                hud.LanguageToggled += () => SetLanguage(Core.Language == "ar" ? "en" : "ar");
                hud.MicModeToggled += () => { if (voice) voice.SetMode(voice.mode == SurgeryVoiceInput.Mode.HandsFree ? SurgeryVoiceInput.Mode.PushToTalk : SurgeryVoiceInput.Mode.HandsFree); };
                hud.SetBanner(Scenario.ValidationBanner(Core.Language));
            }
            RefreshMic();

            Live = SurgeryLiveClient.FromUserConfig();
            Live.Language = Core.Language;
            Live.StatusChanged += (on, err) => RefreshStatus();
            var startTask = Live.StartSession(Scenario);
            while (!startTask.IsCompleted) yield return null;
            RefreshStatus();
            Core.Start(Core.Language);
            RefreshObjective();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (bus != null) bus.EventPublished -= OnBusEvent;
            InterruptSpeech();
            _ = Live?.EndSession();
        }

        void Update()
        {
            if (Core == null || !Core.Active) return;
            if (Time.time >= nextTick) { nextTick = Time.time + tickSeconds; Core.Tick(); if (UsingMockMechanics && Core.Runtime is MockScenarioRuntime) { } }
            if (!speaking && queue.Count > 0) StartCoroutine(SpeakRoutine(queue.Dequeue()));
        }

        // ------------------------------------------------------------------ events → context, HUD
        void OnBusEvent(SurgeryEvent e)
        {
            if (e.id == SurgeryEventIds.ObjectiveStarted || e.id == SurgeryEventIds.ObjectiveCompleted || e.id == SurgeryEventIds.SessionStarted || e.id == SurgeryEventIds.SessionReset) { RefreshObjective(); Live?.PostContext(Context()); }
        }

        SurgeryContextDto Context()
        {
            var completed = Core.Runtime?.CompletedObjectives?.ToArray() ?? new string[0];
            var recent = bus.Log.Where(x => SurgeryEventIds.IsGameplay(x.id)).Reverse().Take(6).Select(x => x.id + (string.IsNullOrEmpty(x.toolId) ? "" : ":" + x.toolId)).Reverse().ToArray();
            var holding = bus.Log.LastOrDefault(x => x.id == SurgeryEventIds.ToolGrabbed || x.id == SurgeryEventIds.ToolReleased);
            return new SurgeryContextDto { objectiveId = Core.Runtime?.CurrentObjectiveId ?? Core.Supervisor.CurrentObjectiveId, completed = completed, recent = recent, holding = holding != null && holding.id == SurgeryEventIds.ToolGrabbed ? holding.toolId : "" };
        }

        void RefreshObjective()
        {
            if (!hud) return;
            var id = Core.Runtime?.CurrentObjectiveId ?? Core.Supervisor.CurrentObjectiveId;
            var o = Scenario.Objective(id); var ordered = Scenario.Ordered.ToList();
            if (o == null) hud.SetObjective(Core.Completed ? (Core.Language == "ar" ? "اكتملت العملية الافتراضية" : "Simulated procedure complete") : (Core.Language == "ar" ? "استعد" : "Get ready"), "", ordered.Count, ordered.Count);
            else hud.SetObjective(o.title.Get(Core.Language), o.explanation.Get(Core.Language), ordered.IndexOf(o), ordered.Count);
        }

        void RefreshStatus()
        {
            if (!hud) return;
            string ai = Live != null && Live.Online ? (Core.Language == "ar" ? "الذكاء الاصطناعي: متصل" : "Live AI: online")
                : (Core.Language == "ar" ? "نص فقط (الذكاء الاصطناعي غير متصل)" : "Text-only (Live AI offline)") + (string.IsNullOrEmpty(Live?.LastError) ? "" : " — " + Live.LastError);
            if (UsingMockMechanics) ai += Core.Language == "ar" ? " · ميكانيكا تجريبية (MOCK)" : " · MOCK mechanics";
            hud.SetStatus(ai, Live != null && Live.Online && !UsingMockMechanics ? new Color(0.4f, 1f, 0.85f) : new Color(1f, 0.75f, 0.3f));
        }

        void RefreshMic()
        {
            if (!hud) return;
            if (!voice) { hud.SetMic(""); return; }
            hud.SetMic(voice.mode == SurgeryVoiceInput.Mode.HandsFree ? (Core.Language == "ar" ? "🎤 بدون إيدين (M للتبديل)" : "🎤 Hands-free (M to switch)")
                : (Core.Language == "ar" ? "🎤 اضغط T أو A/X واحكي" : "🎤 Hold T or A/X to talk"));
        }

        // ------------------------------------------------------------------ speech out
        SurgicalCharacter Char(string id) => actuator ? actuator.Get(id) : null;
        string NameOf(string id) => Scenario.Persona(id)?.displayName.Get(Core.Language) ?? id;

        IEnumerator SpeakRoutine(CharacterLine line)
        {
            speaking = true;
            var c = Char(line.characterId); var sp = c ? c.Speech : null;
            hud?.AddLine(NameOf(line.characterId), line.text);
            bool online = Live != null && Live.Online && !string.IsNullOrEmpty(Live.SessionId);
            if (online && sp)
            {
                var stream = new StreamingVoice("surgery_" + line.characterId); string spoken = line.text;
                var req = line.kind == "debrief"
                    ? Live.SayStream(line.characterId, "debrief", line.text, false, stream, m => spoken = string.IsNullOrEmpty(m?.text) ? spoken : m.text, "", Core.Result != null ? SurgicalDebriefComposer.Facts(Core.Result) : null)
                    : Live.SayStream(line.characterId, line.kind, line.text, line.approvedOnly, stream, m => spoken = string.IsNullOrEmpty(m?.text) ? spoken : m.text, line.kind == "safety" ? "safety" : "");
                currentReq = req; currentStream = stream; currentSpeaker = sp;
                req.SendWebRequest();
                float t0 = Time.time; while (!req.isDone && stream.FirstAudioTime < 0 && Time.time - t0 < 8f) yield return null;
                if (stream.FirstAudioTime >= 0 || req.isDone && req.result == UnityWebRequest.Result.Success) sp.SpeakStreaming(spoken, stream);
                while (!req.isDone) yield return null;
                stream.Complete(); Live.ReportStreamResult(req);
                if (req.result != UnityWebRequest.Result.Success && !stream.Cancelled) sp.Speak(line.text);   // text-only fallback, clearly subtitles
                while (sp.IsSpeaking) yield return null;
                req.Dispose(); currentReq = null; currentStream = null;
            }
            else if (sp) { sp.Speak(line.text); while (sp.IsSpeaking) yield return null; }
            else yield return new WaitForSeconds(Mathf.Clamp(line.text.Length / 15f, 1.5f, 8f));
            currentSpeaker = null; speaking = false;
        }

        /// <summary>Intentional interruption: the student starts talking → stop the current character line immediately.</summary>
        void InterruptSpeech()
        {
            try { currentReq?.Abort(); } catch { }
            currentStream?.Cancel();
            if (currentSpeaker) currentSpeaker.Stop();
        }

        // ------------------------------------------------------------------ speech in
        string AddressedCharacter()
        {
            // The character the student is looking at; the surgeon by default.
            var cam = Camera.main; if (!cam || !actuator) return SurgeryCharacterIds.Surgeon;
            float best = 25f; string who = SurgeryCharacterIds.Surgeon;
            foreach (var c in new[] { actuator.surgeon, actuator.assistant })
            {
                if (!c) continue; var to = c.transform.position + Vector3.up * 1.5f - cam.transform.position;
                float a = Vector3.Angle(cam.transform.forward, to); if (a < best) { best = a; who = c.characterId; }
            }
            return who;
        }

        void OnUtterance(byte[] pcm)
        {
            if (Core == null || !Core.Active || voiceTurnBusy) return;
            if (Live == null || !Live.Online || string.IsNullOrEmpty(Live.SessionId))
            {
                hud?.AddLine(Core.Language == "ar" ? "النظام" : "System", Core.Language == "ar" ? "الصوت بحتاج اتصال بالذكاء الاصطناعي. اضغط H للمساعدة أو J لشرح الخطوة." : "Voice needs the Live AI proxy. Press H for help or J to explain the current step.");
                return;
            }
            StartCoroutine(VoiceTurn(AddressedCharacter(), pcm));
        }

        IEnumerator VoiceTurn(string characterId, byte[] pcm)
        {
            voiceTurnBusy = true;
            var c = Char(characterId); var stream = new StreamingVoice("surgery_reply"); SurgeryTurnResponse meta = null;
            var req = Live.VoiceTurn(characterId, pcm, Context(), stream, m => meta = m);
            currentReq = req; currentStream = stream; req.SendWebRequest();
            while (meta == null && !req.isDone) yield return null;
            if (meta != null && !meta.noSpeech)
            {
                if (!string.IsNullOrEmpty(meta.heard)) hud?.AddLine(Core.Language == "ar" ? "إنت" : "You", meta.heard);
                if (!string.IsNullOrEmpty(meta.lang) && meta.lang != Core.Language && string.IsNullOrEmpty(Live.LangLock)) { /* follow the student's language for this reply only */ }
                var intent = ToIntent(meta, characterId);
                if (!string.IsNullOrEmpty(intent.languageCommand)) SetLanguage(intent.languageCommand, lockIt: true);
                if (!string.IsNullOrEmpty(meta.text) && c && c.Speech) { hud?.AddLine(NameOf(characterId), meta.text); currentSpeaker = c.Speech; c.Speech.SpeakStreaming(meta.text, stream); }
                if (meta.deferred || intent.helpRequest || intent.explainRequest || intent.action.action != SurgeryActionCatalog.None) Core.HandleRequest(intent);
            }
            while (!req.isDone) yield return null;
            stream.Complete(); Live.ReportStreamResult(req);
            if (c && c.Speech) while (c.Speech.IsSpeaking) yield return null;
            req.Dispose(); currentReq = null; currentStream = null; currentSpeaker = null; voiceTurnBusy = false;
        }

        /// <summary>Maps the proxy's proposed intent to the deterministic core's intent (the core re-validates everything).</summary>
        SurgeryIntentParser.Intent ToIntent(SurgeryTurnResponse m, string addressed)
        {
            var i = m.intent ?? new SurgeryIntentDto();
            var it = SurgeryIntentParser.Parse(m.heard ?? "", Scenario);          // deterministic safety net
            if (m.intent != null)
            {
                it.addressed = string.IsNullOrEmpty(i.characterId) ? addressed : i.characterId;
                if (!string.IsNullOrEmpty(i.action) && i.action != "none") { it.action.characterId = it.addressed; it.action.action = i.action; it.action.instrumentId = i.instrumentId ?? ""; it.action.forCurrentObjective = i.forCurrentObjective; }
                it.helpRequest |= i.help; it.explainRequest |= i.explain;
                if (!string.IsNullOrEmpty(i.languageCommand)) it.languageCommand = i.languageCommand;
            }
            it.action.utterance = m.heard ?? "";
            return it;
        }

        // ------------------------------------------------------------------ session lifecycle
        void SetLanguage(string lang, bool lockIt = false)
        {
            Core.Language = lang; if (Live != null) { Live.Language = Core.Language; if (lockIt) Live.LangLock = Core.Language; }
            ScenarioRouter.Language = Core.Language;
            if (hud) { hud.Language = Core.Language; hud.Rebuild(); hud.SetBanner(Scenario.ValidationBanner(Core.Language)); }
            RefreshObjective(); RefreshStatus(); RefreshMic();
        }

        void OnCompleted(SurgicalAssessmentResult r)
        {
            var written = SurgicalDebriefComposer.Written(r, Core.Language);
            if (UsingMockMechanics) written = (Core.Language == "ar" ? "⚠ هاي النتيجة من ميكانيكا تجريبية (MOCK)، مش أداء حقيقي.\n\n" : "⚠ MOCK mechanics: this result does not reflect real interactions.\n\n") + written;
            hud?.ShowDebrief(written);
            Debug.Log("[Surgery] Debrief\n" + SurgicalDebriefComposer.Written(r, "en"));
            queue.Enqueue(new CharacterLine { characterId = SurgeryCharacterIds.Surgeon, kind = "debrief", text = SurgicalDebriefComposer.SpokenBase(r, Core.Language), lang = Core.Language });
        }

        public void Restart()
        {
            StopAllCoroutines(); InterruptSpeech(); queue.Clear(); speaking = false; voiceTurnBusy = false;
            hud?.HideDebrief();
            if (actuator) { foreach (var c in new[] { actuator.surgeon, actuator.assistant }) if (c) { c.Interrupt(); c.GoToAnchor(c.homeAnchor, _ => { }); } }
            StartCoroutine(RestartRoutine());
        }
        IEnumerator RestartRoutine()
        {
            var end = Live.EndSession(); while (!end.IsCompleted) yield return null;
            var st = Live.StartSession(Scenario); while (!st.IsCompleted) yield return null;
            Core.Restart(); RefreshStatus(); RefreshObjective();
        }

        public void ReturnToMenu() { InterruptSpeech(); _ = Live?.EndSession(); ScenarioRouter.ToMenu(); }

        // Offline/no-mic shortcuts (also handy in demos): H = help, J = explain current step.
        void LateUpdate()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current; if (kb == null || Core == null || !Core.Active || (hud && hud.Paused)) return;
            if (kb.hKey.wasPressedThisFrame) Core.HandleRequest(new SurgeryIntentParser.Intent { helpRequest = true, addressed = SurgeryCharacterIds.Surgeon });
            if (kb.jKey.wasPressedThisFrame) Core.HandleRequest(new SurgeryIntentParser.Intent { explainRequest = true, addressed = SurgeryCharacterIds.Surgeon });
        }
    }

    /// <summary>Fallback instrument registry from scene tags (used only when mechanics do not provide one).</summary>
    public class SceneInstrumentRegistry : IInstrumentRegistry
    {
        readonly SurgeryCharacterActuator actuator;
        public SceneInstrumentRegistry(SurgeryCharacterActuator a) { actuator = a; }
        IEnumerable<SurgeryInstrumentTag> Tags => UnityEngine.Object.FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None);
        public IReadOnlyList<string> AvailableInstrumentIds => Tags.Select(t => t.instrumentId).Distinct().ToList();
        public bool IsAvailable(string id) => HolderOf(id) == InstrumentHolder.OnTable;
        public InstrumentHolder HolderOf(string id)
        {
            var t = Tags.FirstOrDefault(x => x.instrumentId == id);
            if (!t) return Tags.Any() ? InstrumentHolder.Missing : InstrumentHolder.OnTable;   // no instruments in scene yet (mock scene): don't block
            if (actuator && actuator.assistant && actuator.assistant.Held == t.transform) return InstrumentHolder.Assistant;
            return InstrumentHolder.OnTable;
        }
    }
}
