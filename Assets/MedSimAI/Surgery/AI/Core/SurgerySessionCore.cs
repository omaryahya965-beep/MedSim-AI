using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;

namespace MedSimAI.Surgery.AI
{
    /// <summary>
    /// Tracks an assistant hand-over. "Completed" is declared ONLY when mechanics report tool.grabbed for the same tool
    /// while the offer is open; otherwise it fails after a timeout. The assistant never claims a transfer on its own.
    /// </summary>
    public class HandoffTracker
    {
        readonly ISurgeryEventSink sink; readonly Func<double> now;
        public float TimeoutSeconds = 20;
        public string PendingTool { get; private set; } = "";
        double offeredAt;
        public event Action<string> Completed, Failed;

        public HandoffTracker(ISurgeryEventSink sink, Func<double> clock) { this.sink = sink; now = clock; }

        public void Offer(string toolId)
        {
            PendingTool = toolId ?? ""; offeredAt = now();
            sink?.Publish(new SurgeryEvent(SurgeryEventIds.AssistantHandoffOffered, SurgeryEventSources.Assistant) { toolId = PendingTool, characterId = SurgeryCharacterIds.Assistant });
        }

        public void OnEvent(SurgeryEvent e)
        {
            if (string.IsNullOrEmpty(PendingTool) || e == null) return;
            if (e.id == SurgeryEventIds.SessionReset) { PendingTool = ""; return; }
            bool verified = e.source == SurgeryEventSources.Mechanics || e.source == SurgeryEventSources.Mock;
            if (verified && e.id == SurgeryEventIds.ToolGrabbed && e.toolId == PendingTool)
            {
                var t = PendingTool; PendingTool = "";
                sink?.Publish(new SurgeryEvent(SurgeryEventIds.AssistantHandoffCompleted, SurgeryEventSources.Assistant) { toolId = t, characterId = SurgeryCharacterIds.Assistant });
                Completed?.Invoke(t);
            }
        }

        public void Tick()
        {
            if (string.IsNullOrEmpty(PendingTool) || now() - offeredAt < TimeoutSeconds) return;
            var t = PendingTool; PendingTool = "";
            sink?.Publish(new SurgeryEvent(SurgeryEventIds.AssistantActionFailed, SurgeryEventSources.Assistant) { toolId = t, characterId = SurgeryCharacterIds.Assistant, reason = "not_taken", success = false });
            Failed?.Invoke(t);
        }
    }

    /// <summary>A line a surgical character should say now (approved text; the Unity layer may send it to the proxy to phrase/voice).</summary>
    [Serializable] public class CharacterLine
    {
        public string characterId = "", kind = "", text = "", lang = "en", ruleId = "", objectiveId = "";
        public bool approvedOnly;    // true = speak exactly this text (offline / action acknowledgements)
        public override string ToString() => $"{characterId}[{kind}] {text}";
    }

    /// <summary>
    /// Engine-free orchestrator for ONE surgical session: event bus, supervisor, hand-over tracking, student requests,
    /// completion and assessment. Independent from the ER ClinicalSession: nothing here knows the chest-pain scenario.
    /// The Unity SurgerySessionController wraps this and connects characters, voice and UI.
    /// </summary>
    public class SurgerySessionCore
    {
        public SurgicalScenarioData Scenario { get; }
        public SurgeryEventBus Bus { get; }
        public SurgicalSupervisor Supervisor { get; private set; }
        public HandoffTracker Handoff { get; private set; }
        public ISurgicalScenarioRuntime Runtime;
        public IInstrumentRegistry Instruments;
        public ISurgeryCharacterActuator Actuator;
        public bool AcceptMockAsMechanics;     // tests / mock scenes only
        public string Language { get => lang; set { lang = value == "ar" ? "ar" : "en"; if (Supervisor != null) Supervisor.Language = lang; } }
        public bool Active { get; private set; }
        public bool Completed { get; private set; }
        public SurgicalAssessmentResult Result { get; private set; }
        public readonly List<CharacterLine> Transcript = new List<CharacterLine>();

        public event Action<CharacterLine> Line;                     // characters speak
        public event Action<ValidatedAction, CharacterActionResult> ActionFinished;
        public event Action<SurgicalAssessmentResult> SessionCompleted;

        string lang = "en"; readonly Func<double> clock; readonly HashSet<string> completedObjectives = new HashSet<string>();

        public SurgerySessionCore(SurgicalScenarioData scenario, Func<double> clock, SurgeryEventBus bus = null)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            var errors = SurgicalScenarioValidator.Validate(scenario);
            if (errors.Count > 0) throw new InvalidOperationException("Surgical scenario invalid: " + string.Join("; ", errors));
            this.clock = clock ?? (() => 0);
            Bus = bus ?? new SurgeryEventBus();
            Bus.Clock = this.clock;
            Supervisor = new SurgicalSupervisor(scenario, Bus, this.clock) { Language = lang };
            Supervisor.Speak += u => Say(new CharacterLine { characterId = SurgeryCharacterIds.Surgeon, kind = u.kind, text = u.Text, lang = u.lang, ruleId = u.ruleId, objectiveId = u.objectiveId });
            Handoff = new HandoffTracker(Bus, this.clock);
            Handoff.Failed += t => Say(new CharacterLine { characterId = SurgeryCharacterIds.Assistant, kind = "action", text = SurgeryActionLines.Get("handoff_not_taken", lang), lang = lang, approvedOnly = true });
            Bus.EventPublished += OnBusEvent;
        }

        public string Start(string language = null)
        {
            if (language != null) Language = language;
            var id = Guid.NewGuid().ToString("N");
            Bus.BeginSession(id);
            Supervisor.Reset(); Supervisor.Language = lang; completedObjectives.Clear(); Transcript.Clear();
            Active = true; Completed = false; Result = null;
            Bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionStarted, SurgeryEventSources.Integration));
            return id;
        }

        /// <summary>Restart: session.reset, mechanics reset, fresh bus session. No state from the previous run survives.</summary>
        public string Restart()
        {
            if (Active) Bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionReset, SurgeryEventSources.Integration));
            Active = false;
            var id = Start();
            // Integration fix: reset the mechanics AFTER the new bus session began, so its objective.started (objective 1)
            // is part of the new session log (before, BeginSession cleared it and the supervisor had no current objective).
            Runtime?.ResetForNewSession();
            return id;
        }

        public void End() { Active = false; }

        public void Tick() { if (!Active) return; Supervisor.Tick(); Handoff.Tick(); }

        void OnBusEvent(SurgeryEvent e)
        {
            Supervisor.OnEvent(e);
            Handoff.OnEvent(e);
            bool verified = e.source == SurgeryEventSources.Mechanics || (AcceptMockAsMechanics && e.source == SurgeryEventSources.Mock);
            if (verified && e.id == SurgeryEventIds.ObjectiveCompleted) completedObjectives.Add(e.objectiveId);
            if (Active && !Completed && verified && e.id == SurgeryEventIds.ObjectiveCompleted && Scenario.objectives.All(o => completedObjectives.Contains(o.id))) Complete();
        }

        /// <summary>Ends the run (all objectives done, or the student chose to finish) and computes the assessment.</summary>
        public SurgicalAssessmentResult Complete()
        {
            if (Completed) return Result;
            Completed = true;
            Bus.Publish(new SurgeryEvent(SurgeryEventIds.SessionCompleted, SurgeryEventSources.Integration));
            Supervisor.Tick();   // lets the completion line out
            Result = SurgicalAssessmentEngine.Evaluate(Scenario, Bus.Log, AcceptMockAsMechanics);
            SessionCompleted?.Invoke(Result);
            return Result;
        }

        /// <summary>
        /// A student request already understood (by the AI's structured output or the offline parser). Validates and executes
        /// it, publishes the lifecycle events, and returns the validated action. Help/explain/language are handled here too.
        /// </summary>
        public ValidatedAction HandleRequest(SurgeryIntentParser.Intent intent)
        {
            if (intent == null) return new ValidatedAction();
            if (!string.IsNullOrEmpty(intent.languageCommand)) Language = intent.languageCommand;
            if (intent.helpRequest) Bus.Publish(new SurgeryEvent(SurgeryEventIds.HelpRequested, SurgeryEventSources.AI) { characterId = SurgeryCharacterIds.Surgeon, objectiveId = Supervisor.CurrentObjectiveId });
            if (intent.explainRequest) Say(new CharacterLine { characterId = SurgeryCharacterIds.Surgeon, kind = "explain", text = Supervisor.ExplainCurrentObjective(lang), lang = lang, objectiveId = Supervisor.CurrentObjectiveId });

            var v = SurgeryActionValidator.Validate(intent.action, new ActionContext { Scenario = Scenario, CurrentObjectiveId = Runtime?.CurrentObjectiveId ?? Supervisor.CurrentObjectiveId, Instruments = Instruments });
            if (v.status == ActionStatus.None || SurgeryActionCatalog.IsVerbal(v.action)) return v;
            Bus.Publish(new SurgeryEvent(SurgeryEventIds.AssistantActionRequested, SurgeryEventSources.Assistant) { characterId = v.characterId, toolId = v.instrumentId, reason = v.action + ":" + v.status, success = v.status == ActionStatus.Accepted });

            var name = Scenario.Instrument(v.instrumentId)?.name.Get(lang) ?? "";
            if (v.status == ActionStatus.RedirectToSupervisor)
            {
                Say(new CharacterLine { characterId = SurgeryCharacterIds.Assistant, kind = "action", text = SurgeryActionLines.Get(v.lineKey, lang), lang = lang, approvedOnly = true });
                Bus.Publish(new SurgeryEvent(SurgeryEventIds.HelpRequested, SurgeryEventSources.AI) { characterId = SurgeryCharacterIds.Surgeon, objectiveId = Supervisor.CurrentObjectiveId });
                return v;
            }
            if (!string.IsNullOrEmpty(v.lineKey)) Say(new CharacterLine { characterId = v.characterId, kind = "action", text = SurgeryActionLines.Get(v.lineKey, lang, name), lang = lang, approvedOnly = true });
            if (v.status != ActionStatus.Accepted) return v;

            if (Actuator == null || !Actuator.Supports(v.characterId, v.action))
            {
                Finish(v, new CharacterActionResult { characterId = v.characterId, action = v.action, instrumentId = v.instrumentId, outcome = ActionOutcome.Unavailable, reason = "no_actuator" });
                return v;
            }
            Actuator.Execute(v.ToRequest(intent.action.utterance), res => Finish(v, res));
            return v;
        }

        void Finish(ValidatedAction v, CharacterActionResult res)
        {
            res ??= new CharacterActionResult { outcome = ActionOutcome.Failed };
            bool handOver = v.action == SurgeryActionCatalog.HandInstrument;
            if (handOver && (res.outcome == ActionOutcome.Completed || res.outcome == ActionOutcome.Started))
                Handoff.Offer(v.instrumentId);   // assistant now holds it out; completion only on tool.grabbed
            else
                Bus.Publish(new SurgeryEvent(res.outcome == ActionOutcome.Completed ? SurgeryEventIds.AssistantActionCompleted : SurgeryEventIds.AssistantActionFailed, SurgeryEventSources.Assistant)
                { characterId = v.characterId, toolId = v.instrumentId, reason = v.action + ":" + res.outcome, success = res.outcome == ActionOutcome.Completed });
            if (res.outcome == ActionOutcome.Unavailable || res.outcome == ActionOutcome.Failed)
                Say(new CharacterLine { characterId = v.characterId, kind = "action", text = SurgeryActionLines.Get("action_unavailable", lang), lang = lang, approvedOnly = true });
            ActionFinished?.Invoke(v, res);
        }

        void Say(CharacterLine l) { Transcript.Add(l); Line?.Invoke(l); }
    }
}
