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
        [Tooltip("Hospital scene: the hospital coordinator owns navigation. 'Return to menu' goes back to the corridor instead of loading the menu scene.")]
        public bool hostManaged;
        /// <summary>Hospital scene: the student asked to leave the operating room (pause menu / F10).</summary>
        public event Action HostReturnRequested;
        /// <summary>Hospital scene: sees every understood utterance first (navigation such as "take me back to room one"). Return true to consume it.</summary>
        public static Func<string, bool> UtteranceInterceptor;
        /// <summary>Host hook: what the surgical voice path heard the student say (the hospital shows it in its shared conversation panel).</summary>
        public static Action<string> StudentHeard;
        /// <summary>The student asked to stop a running demonstration ("enough, let me try").</summary>
        public static event Action StopDemonstrationRequested;
        /// <summary>No character line is playing or queued (demonstration narration waits for this).</summary>
        public bool SpeechIdle => !speaking && queue.Count == 0 && !voiceTurnBusy;

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
            // Prefer the mechanics root (it resets anatomy, instruments, implants and objectives together); a bare engine or
            // instrument controller also implements the interfaces, and scene order must not decide which one we get.
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            var runtime = behaviours.OfType<ISurgicalScenarioRuntime>().OrderBy(r => r is MedSimAI.Surgery.Mechanics.SurgeryMechanicsRoot ? 0 : 1).FirstOrDefault();
            var registry = behaviours.OfType<IInstrumentRegistry>().OrderBy(r => r is MedSimAI.Surgery.Mechanics.SurgeryMechanicsRoot ? 0 : 1).FirstOrDefault();
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
                hud.SetBanner("");   // no draft/validation ribbon in the live experience (status documented in Docs/)
            }
            RefreshMic();

            Live = SurgeryLiveClient.FromUserConfig();
            Live.Language = Core.Language;
            Live.StatusChanged += (on, err) => RefreshStatus();
            var startTask = Live.StartSession(Scenario);
            while (!startTask.IsCompleted) yield return null;
            RefreshStatus();
            Core.Start(Core.Language);
            // Integration fix: (re)start the real mechanics inside the new bus session so objective.started (objective 1) is logged.
            if (!UsingMockMechanics) Core.Runtime?.ResetForNewSession();
            RefreshObjective();
        }

        /// <summary>
        /// Hospital scene: the student left the operating room. Stops speech and queued lines, closes the live surgical session
        /// and marks the core inactive. A later visit calls Restart(), which starts a fresh session (nothing carries over).
        /// </summary>
        public void Suspend()
        {
            StopAllCoroutines(); InterruptSpeech(); queue.Clear(); speaking = false; voiceTurnBusy = false;
            hud?.HideDebrief(); hud?.ShowPause(false);
            Core?.End();
            _ = Live?.EndSession();
        }

        /// <summary>Queues a line for a character (hospital guide messages inside the operating room). Approved text, spoken as is.</summary>
        public void Say(string characterId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            queue.Enqueue(new CharacterLine { characterId = characterId, kind = "guide", text = text, lang = Core != null ? Core.Language : "en", approvedOnly = true });
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
            if (o == null) hud.SetObjective(Core.Completed ? "Simulated procedure complete" : "Get ready", "", ordered.Count, ordered.Count);
            else hud.SetObjective(o.title.Get("en"), ToolLine(o) + o.explanation.Get(Core.Language), ordered.IndexOf(o), ordered.Count);   // step names always in English
        }

        /// <summary>"Tool: skin marker → marked site on the right knee" (first line of the step panel), from the authored scenario.</summary>
        string ToolLine(SurgicalObjective o)
        {
            bool ar = Core.Language == "ar";
            var tools = (o.requiredTools ?? new string[0]).Select(t => Scenario.Instrument(t)?.name.Get(Core.Language) ?? t).ToList();
            if (!string.IsNullOrEmpty(o.implantId)) tools.Insert(0, Scenario.implants.FirstOrDefault(i => i.id == o.implantId)?.name.Get(Core.Language) ?? o.implantId);
            var where = o.targets != null && o.targets.Length > 0 ? Scenario.targets.FirstOrDefault(t => t.id == o.targets[0])?.name.Get(Core.Language) : null;
            if (tools.Count == 0) return "";
            return (ar ? "<b>الأداة:</b> " : "<b>Tool:</b> ") + string.Join(ar ? " ثم " : " then ", tools) + (string.IsNullOrEmpty(where) ? "" : (ar ? " · المكان: " : " · Where: ") + where) + "\n";
        }

        void RefreshStatus()
        {
            if (!hud) return;
            // Always English and short: "AI connected" / "AI offline · text only".
            string ai = Live != null && Live.Online ? "AI connected" : "AI offline · text only" + (string.IsNullOrEmpty(Live?.LastError) ? "" : " (" + Live.LastError + ")");
            if (UsingMockMechanics) ai += " · MOCK mechanics";
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
                if (!string.IsNullOrEmpty(meta.heard)) { hud?.AddLine(Core.Language == "ar" ? "إنت" : "You", meta.heard); StudentHeard?.Invoke(meta.heard); }
                if (!string.IsNullOrEmpty(meta.heard) && UtteranceInterceptor != null && UtteranceInterceptor(meta.heard))
                {   // consumed by the hospital (navigation): no surgical reply
                    stream.Cancel(); try { req.Abort(); } catch { } req.Dispose(); currentReq = null; currentStream = null; voiceTurnBusy = false; yield break;
                }
                if ((meta.lang == "ar" || meta.lang == "en") && meta.lang != Core.Language && string.IsNullOrEmpty(Live.LangLock)) SetLanguage(meta.lang);   // no explicit language choice: follow the language the student just spoke (as the clinical room does)
                var intent = ToIntent(meta, characterId);
                if (!string.IsNullOrEmpty(intent.languageCommand)) SetLanguage(intent.languageCommand, lockIt: true);
                if (!string.IsNullOrEmpty(meta.text) && c && c.Speech) { hud?.AddLine(NameOf(characterId), meta.text); currentSpeaker = c.Speech; c.Speech.SpeakStreaming(meta.text, stream); }
                if (intent.stopDemo) StopDemonstrationRequested?.Invoke();
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
            if (hud) { hud.Language = Core.Language; hud.Rebuild(); hud.SetBanner(""); }
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

        public void ReturnToMenu()
        {
            if (hostManaged) { InterruptSpeech(); HostReturnRequested?.Invoke(); return; }
            InterruptSpeech(); _ = Live?.EndSession(); ScenarioRouter.ToMenu();
        }

        // Offline/no-mic shortcuts (also handy in demos): H = help, J = explain current step.
        void LateUpdate()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current; if (kb == null || Core == null || !Core.Active || (hud && hud.Paused)) return;
            if (kb.hKey.wasPressedThisFrame) Core.HandleRequest(new SurgeryIntentParser.Intent { helpRequest = true, addressed = SurgeryCharacterIds.Surgeon });
            if (kb.jKey.wasPressedThisFrame) Core.HandleRequest(new SurgeryIntentParser.Intent { explainRequest = true, addressed = SurgeryCharacterIds.Surgeon });
            // G = "show me this step", Shift+G = "show me the whole procedure" (same validated path as the spoken request)
            if (kb.gKey.wasPressedThisFrame)
            {
                bool full = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                Core.HandleRequest(new SurgeryIntentParser.Intent { addressed = SurgeryCharacterIds.Surgeon, action = new ProposedAction { characterId = SurgeryCharacterIds.Surgeon, action = full ? SurgeryActionCatalog.DemonstrateProcedure : SurgeryActionCatalog.DemonstrateStep, utterance = full ? "show me the whole procedure" : "show me this step" } });
            }
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
