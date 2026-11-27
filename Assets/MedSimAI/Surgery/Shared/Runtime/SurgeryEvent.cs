using System;
using System.Collections.Generic;

namespace MedSimAI.Surgery.Shared
{
    /// <summary>
    /// One structured gameplay event. Flat, JsonUtility-serialisable fields only (no dictionaries) so it can be logged,
    /// replayed in tests and sent to the AI proxy as-is. Unused string fields stay empty; unused numbers stay at -1.
    /// </summary>
    [Serializable]
    public class SurgeryEvent
    {
        public string id = "";           // SurgeryEventIds.*
        public string source = "";       // SurgeryEventSources.*
        public string sessionId = "";    // set by the bus
        public long seq;                 // set by the bus (monotonic per session)
        public float t = -1;             // seconds since session.started (set by the bus if < 0)
        public string objectiveId = "";  // scenario objective id
        public string toolId = "";       // InstrumentIds.*
        public string targetId = "";     // AnatomyTargetIds.*
        public string implantId = "";    // ImplantIds.*
        public string state = "";        // AnatomyStates.* for anatomy.state_changed
        public string reason = "";       // RejectReasons.* for interaction.rejected
        public string characterId = "";  // SurgeryCharacterIds.* for assistant/supervisor events
        public bool success = true;
        public float positionErrorMm = -1;   // placement accuracy (implant/guide), -1 = not measured
        public float angleErrorDeg = -1;     // placement angle error, -1 = not measured
        public string detail = "";       // free diagnostic text for logs ONLY — never parsed, never shown to the AI

        public SurgeryEvent() { }
        public SurgeryEvent(string id, string source) { this.id = id; this.source = source; }

        public SurgeryEvent Clone() => (SurgeryEvent)MemberwiseClone();
        public override string ToString() =>
            $"#{seq} {t:0.0}s {id} [{source}] obj={objectiveId} tool={toolId} target={targetId} implant={implantId} state={state} reason={reason} ok={success}";

        // ---- convenience factories (used by mechanics and by tests) ----
        public static SurgeryEvent Tool(string id, string toolId, string objectiveId = "", string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(id, source) { toolId = toolId, objectiveId = objectiveId };
        public static SurgeryEvent Anatomy(string targetId, string toolId, string objectiveId = "", string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(SurgeryEventIds.AnatomyInteracted, source) { targetId = targetId, toolId = toolId, objectiveId = objectiveId };
        public static SurgeryEvent StateChanged(string targetId, string state, string objectiveId = "", string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(SurgeryEventIds.AnatomyStateChanged, source) { targetId = targetId, state = state, objectiveId = objectiveId };
        public static SurgeryEvent Rejected(string reason, string toolId = "", string targetId = "", string objectiveId = "", string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(SurgeryEventIds.InteractionRejected, source) { reason = reason, toolId = toolId, targetId = targetId, objectiveId = objectiveId, success = false };
        public static SurgeryEvent Objective(string id, string objectiveId, string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(id, source) { objectiveId = objectiveId };
        public static SurgeryEvent Implant(string id, string implantId, string targetId, float posErrMm = -1, float angErrDeg = -1, bool success = true, string objectiveId = "", string source = SurgeryEventSources.Mechanics)
            => new SurgeryEvent(id, source) { implantId = implantId, targetId = targetId, positionErrorMm = posErrMm, angleErrorDeg = angErrDeg, success = success, objectiveId = objectiveId };
    }

    public interface ISurgeryEventSink { void Publish(SurgeryEvent e); }

    public interface ISurgeryEventSource
    {
        event Action<SurgeryEvent> EventPublished;
        IReadOnlyList<SurgeryEvent> Log { get; }
    }

    /// <summary>
    /// The single in-process event bus for one surgical session. Pure C#: no network, no AI. Subscriber exceptions are
    /// caught and reported so an AI/UI failure can never break mechanics or corrupt the log.
    /// </summary>
    public class SurgeryEventBus : ISurgeryEventSink, ISurgeryEventSource
    {
        readonly List<SurgeryEvent> log = new List<SurgeryEvent>();
        long seq;
        double startedAt = double.NaN;
        public string SessionId { get; private set; } = "";
        public Func<double> Clock = () => DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        public Action<string> OnSubscriberError = _ => { };
        /// <summary>When false (default), unknown event ids are rejected (logged to OnSubscriberError) instead of published.</summary>
        public bool AllowUnknownIds;

        public event Action<SurgeryEvent> EventPublished;
        public IReadOnlyList<SurgeryEvent> Log => log;

        public void BeginSession(string sessionId)
        {
            log.Clear(); seq = 0; SessionId = sessionId ?? ""; startedAt = Clock();
        }

        public double Elapsed => double.IsNaN(startedAt) ? 0 : Clock() - startedAt;

        public void Publish(SurgeryEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.id)) return;
            if (!AllowUnknownIds && !SurgeryEventIds.IsKnown(e.id)) { OnSubscriberError("unknown event id: " + e.id); return; }
            var copy = e.Clone();               // publishers cannot mutate the logged record afterwards
            copy.sessionId = SessionId;
            copy.seq = ++seq;
            if (copy.t < 0) copy.t = (float)Elapsed;
            log.Add(copy);
            var handlers = EventPublished;
            if (handlers == null) return;
            foreach (Action<SurgeryEvent> h in handlers.GetInvocationList())
            {
                try { h(copy.Clone()); }
                catch (Exception ex) { OnSubscriberError($"{h.Method.DeclaringType?.Name}.{h.Method.Name}: {ex.Message}"); }
            }
        }
    }
}
