using System;
using System.Collections.Generic;
using System.Linq;

namespace MedSimAI.Surgery.Shared.Mocks
{
    // Mock implementations of every shared interface. Use them in your own test scene / tests so you never wait for
    // another person's branch. They are deliberately simple and deterministic. Source tag: SurgeryEventSources.Mock.

    public class ManualClock : ISurgeryClock { public double Now { get; set; } public void Advance(double s) => Now += s; }

    public class MockInstrumentRegistry : IInstrumentRegistry
    {
        readonly Dictionary<string, InstrumentHolder> holders = new Dictionary<string, InstrumentHolder>();
        public MockInstrumentRegistry(IEnumerable<string> ids = null) { foreach (var id in ids ?? InstrumentIds.All) holders[id] = InstrumentHolder.OnTable; }
        public IReadOnlyList<string> AvailableInstrumentIds => holders.Keys.ToList();
        public bool IsAvailable(string id) => holders.TryGetValue(id, out var h) && h == InstrumentHolder.OnTable;
        public InstrumentHolder HolderOf(string id) => holders.TryGetValue(id, out var h) ? h : InstrumentHolder.Missing;
        public void Set(string id, InstrumentHolder h) => holders[id] = h;
    }

    public class MockAnatomyStateView : IAnatomyStateView
    {
        readonly List<string> supported;
        public MockAnatomyStateView(string targetId, params string[] states) { TargetId = targetId; supported = states.Length > 0 ? states.ToList() : new List<string> { AnatomyStates.Intact }; CurrentState = supported[0]; }
        public string TargetId { get; }
        public string CurrentState { get; private set; }
        public IReadOnlyList<string> SupportedStates => supported;
        public bool SetState(string s) { if (!supported.Contains(s)) return false; CurrentState = s; return true; }
    }

    /// <summary>
    /// Linear objective runner that listens to the bus and completes an objective when its expected event arrives.
    /// Stand-in for Person 3's real mechanics until feature/surgery-mechanics is merged.
    /// </summary>
    public class MockScenarioRuntime : ISurgicalScenarioRuntime
    {
        readonly List<string> order; readonly List<string> done = new List<string>(); int index;
        public MockScenarioRuntime(string scenarioId, IEnumerable<string> objectiveOrder) { ScenarioId = scenarioId; order = objectiveOrder.ToList(); }
        public string ScenarioId { get; }
        public string CurrentObjectiveId => index < order.Count ? order[index] : "";
        public bool IsObjectiveComplete(string id) => done.Contains(id);
        public IReadOnlyList<string> CompletedObjectives => done;
        public bool IsFinished => index >= order.Count;
        public void ResetForNewSession() { done.Clear(); index = 0; }
        /// <summary>Marks the current objective complete (call it when your mock condition is met) and returns the events to publish.</summary>
        public IEnumerable<SurgeryEvent> CompleteCurrent()
        {
            if (IsFinished) yield break;
            var id = order[index]; done.Add(id); index++;
            yield return SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, id, SurgeryEventSources.Mock);
            if (!IsFinished) yield return SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, order[index], SurgeryEventSources.Mock);
        }
    }

    /// <summary>Records requests and completes them immediately (or with a scripted outcome).</summary>
    public class MockCharacterActuator : ISurgeryCharacterActuator
    {
        public readonly List<CharacterActionRequest> Executed = new List<CharacterActionRequest>();
        public Func<CharacterActionRequest, ActionOutcome> Outcome = _ => ActionOutcome.Completed;
        public bool Supports(string characterId, string action) => true;
        public void Execute(CharacterActionRequest r, Action<CharacterActionResult> onDone)
        {
            Executed.Add(r);
            onDone?.Invoke(new CharacterActionResult { characterId = r.characterId, action = r.action, instrumentId = r.instrumentId, outcome = Outcome(r) });
        }
    }

    /// <summary>Ready-made event scripts for supervisor/assessment fixtures (source = mock unless overridden).</summary>
    public static class MockEventScripts
    {
        public static SurgeryEvent[] PerfectIncision(string objectiveId, string source = SurgeryEventSources.Mock) => new[] {
            SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, objectiveId, source),
            SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Scalpel, objectiveId, source),
            SurgeryEvent.Anatomy(AnatomyTargetIds.IncisionLine, InstrumentIds.Scalpel, objectiveId, source),
            SurgeryEvent.StateChanged(AnatomyTargetIds.IncisionLine, AnatomyStates.Incised, objectiveId, source),
            SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, objectiveId, source),
        };

        public static SurgeryEvent[] WrongToolThenCorrect(string objectiveId, string source = SurgeryEventSources.Mock) => new[] {
            SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, objectiveId, source),
            SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.OscillatingSaw, objectiveId, source),
            SurgeryEvent.Rejected(RejectReasons.WrongTool, InstrumentIds.OscillatingSaw, AnatomyTargetIds.IncisionLine, objectiveId, source),
            SurgeryEvent.Tool(SurgeryEventIds.ToolReleased, InstrumentIds.OscillatingSaw, objectiveId, source),
            SurgeryEvent.Tool(SurgeryEventIds.ToolGrabbed, InstrumentIds.Scalpel, objectiveId, source),
            SurgeryEvent.Anatomy(AnatomyTargetIds.IncisionLine, InstrumentIds.Scalpel, objectiveId, source),
            SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, objectiveId, source),
        };
    }
}
