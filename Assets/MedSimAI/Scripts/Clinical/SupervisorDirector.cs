using System;
using System.Collections.Generic;
using System.Linq;

namespace MedSimAI.Clinical
{
    public class InterventionRequest
    {
        public InterventionRule rule; public int level; public string line; public string focus; public float time; public bool safety; public string detail;
    }

    /// <summary>
    /// Event-driven clinical supervisor. Observes the authoritative event log and decides WHEN the supervising doctor
    /// should speak, using scenario-authored rules (interventions[] in the scenario JSON):
    ///  • correct actions → silence (no rule ever triggers on a correct action),
    ///  • ordinary mistakes → grace period for self-correction, cancel if corrected,
    ///  • safety-critical rules → priority over everything else, bypass the global cooldown,
    ///  • per-rule cooldowns, repeat limits with an escalating hint ladder, global cooldown between unprompted remarks,
    ///  • never interrupts while anyone is speaking or the student is talking (CanSpeak gate).
    /// Pure C#, deterministic, unit-tested. It never calls the AI; the AI only phrases the chosen approved line.
    /// </summary>
    public class SupervisorDirector
    {
        class RuleState { public int fired; public float lastFired = -1e9f; public bool pending; public float dueAt; public float armedAt; public string detail; }

        readonly ClinicalSession s;
        readonly Dictionary<string, RuleState> state = new Dictionary<string, RuleState>();
        public Func<bool> CanSpeak = () => true;
        public event Action<InterventionRequest> Intervene;
        public bool Enabled = true;
        /// <summary>Assessment mode: only safety-critical rules may interrupt.</summary>
        public bool AllowOrdinary = true;
        public float StalePendingSeconds = 45f;
        readonly List<InterventionRequest> history = new List<InterventionRequest>();
        public IReadOnlyList<InterventionRequest> History => history;
        float lastGlobal = -1e9f, lastActivity;
        bool firing;

        SupervisorPolicy Policy => s.Scenario.supervisorPolicy ?? new SupervisorPolicy();
        InterventionRule[] Rules => s.Scenario.interventions ?? new InterventionRule[0];

        public SupervisorDirector(ClinicalSession session)
        {
            s = session;
            s.Tracker.Recorded += OnEvent;
            s.Stages.StageChanged += OnStage;
            s.Patient.ConditionChanged += OnCondition;
            Reset();
        }

        public void Reset()
        {
            state.Clear(); history.Clear(); lastGlobal = -1e9f; lastActivity = 0;
            foreach (var r in Rules) state[r.id] = new RuleState();
        }

        static bool IsAssessment(ClinicalStage st) => ClinicalSession.IsAssessment(st);
        float Now => s.Clock();

        static bool IsActivity(string id) =>
            id.StartsWith("FACT:") || id.StartsWith("EXAM:") || id.StartsWith("INV:") || id.StartsWith("CONCEPT:") ||
            id == ClinicalEvents.InstrumentPicked || id == ClinicalEvents.InstrumentOnPatient || id == ClinicalEvents.ZoneRejected ||
            id == ClinicalEvents.StudentSpoke || id == ClinicalEvents.EcgReviewed || id == ClinicalEvents.EscalationRequested || id == ClinicalEvents.HintRequested;

        bool Suppressed(InterventionRule r)
        {
            var t = r.trigger;
            var unless = t.unlessEvents ?? new string[0];
            if (unless.Length > 0 && (t.unlessAll ? unless.All(s.Tracker.Has) : unless.Any(s.Tracker.Has))) return true;
            var req = t.requireEvents ?? new string[0];
            if (req.Length > 0 && !req.All(s.Tracker.Has)) return true;
            return false;
        }

        bool Eligible(InterventionRule r, RuleState rs, float now) => rs.fired < Math.Max(1, r.maxRepeats) && now - rs.lastFired >= r.cooldownSeconds;

        void Arm(InterventionRule r, float now, string detail = null)
        {
            var rs = state[r.id];
            if (rs.pending || !Eligible(r, rs, now) || Suppressed(r)) return;
            rs.pending = true; rs.armedAt = now; rs.dueAt = now + Math.Max(0, r.graceSeconds); rs.detail = detail;
        }

        void OnEvent(ClinicalActionEvent e)
        {
            if (!Enabled || firing) return;
            if (IsActivity(e.eventId)) lastActivity = e.time;
            foreach (var r in Rules)
            {
                var rs = state[r.id];
                // cancel pending rules the student has now corrected (e.g. asked consent during the grace period)
                if (rs.pending && r.cancelEvents != null && Array.IndexOf(r.cancelEvents, e.eventId) >= 0) rs.pending = false;
                if (r.trigger.type != "event" || r.trigger.eventId != e.eventId) continue;
                if (r.trigger.count > 1)
                {
                    int n = s.Tracker.Log.Count(x => x.eventId == e.eventId && (r.trigger.withinSeconds <= 0 || e.time - x.time <= r.trigger.withinSeconds) && x.time > rs.lastFired);
                    if (n < r.trigger.count) continue;
                }
                Arm(r, e.time, e.detail);
            }
        }

        void OnStage(ClinicalStage from, ClinicalStage to)
        {
            if (!Enabled) return;
            foreach (var r in Rules) if (r.trigger.type == "stage" && r.trigger.stage == to.ToString()) Arm(r, Now);
        }

        void OnCondition(PatientStateDef from, PatientStateDef to, string msg)
        {
            if (!Enabled) return;
            foreach (var r in Rules) if (r.trigger.type == "state" && r.trigger.stateId == to.id) Arm(r, Now);
        }

        /// <summary>Called ~2x per second. Evaluates time-based rules and fires at most one intervention.</summary>
        public InterventionRequest Tick()
        {
            if (!Enabled) return null;
            float now = Now;
            var stage = s.Stages.Current;
            bool active = stage != ClinicalStage.SupervisorFeedback && stage != ClinicalStage.Debriefing;
            if (!active) return null;
            foreach (var r in Rules)
            {
                var rs = state[r.id];
                if (rs.pending) continue;
                if (r.trigger.type == "missing")
                {
                    float? baseT = string.IsNullOrEmpty(r.trigger.sinceEvent) ? 0f : s.Tracker.FirstTime(r.trigger.sinceEvent);
                    if (!baseT.HasValue) continue;
                    if (rs.fired > 0 && r.repeatAfterSeconds <= 0) continue;
                    float due = baseT.Value + r.trigger.afterSeconds + rs.fired * r.repeatAfterSeconds;
                    if (now >= due) Arm(r, now);
                }
                else if (r.trigger.type == "idle" && IsAssessment(stage))
                {
                    float since = Math.Max(lastActivity, rs.lastFired);
                    if (now - since >= r.trigger.afterSeconds) Arm(r, now);
                }
            }
            // choose the highest-priority due candidate
            InterventionRule best = null;
            foreach (var r in Rules)
            {
                var rs = state[r.id];
                if (!rs.pending) continue;
                if (Suppressed(r)) { rs.pending = false; continue; }                         // corrected meanwhile
                if (!r.IsSafety && now - rs.armedAt > r.graceSeconds + StalePendingSeconds) { rs.pending = false; continue; } // no longer relevant
                if (now < rs.dueAt) continue;
                if (!r.IsSafety && !AllowOrdinary) { rs.pending = false; continue; }
                if (best == null || r.priority > best.priority) best = r;
            }
            if (best == null) return null;
            if (!best.IsSafety && now - lastGlobal < Policy.globalCooldownSeconds) return null;
            if (!CanSpeak()) return null; // wait: never talk over the student or another character
            return Fire(best, now);
        }

        InterventionRequest Fire(InterventionRule r, float now)
        {
            var rs = state[r.id];
            int level = Math.Min(rs.fired, r.lines.Length - 1);
            string line = r.lines[level];
            if (line.Contains("{instrumentGuide}"))
            {
                var g = (s.Scenario.instrumentGuides ?? new InstrumentGuide[0]).FirstOrDefault(x => x.instrument == rs.detail);
                line = line.Replace("{instrumentGuide}", g != null ? g.hint : r.lines[0]);
            }
            rs.fired++; rs.lastFired = now; rs.pending = false; lastGlobal = now;
            var req = new InterventionRequest { rule = r, level = level, line = line, focus = r.focus, time = now, safety = r.IsSafety, detail = rs.detail };
            history.Add(req);
            firing = true;
            try { s.Record(ClinicalEvents.Intervention(r.id), "L" + level); } finally { firing = false; }
            Intervene?.Invoke(req);
            return req;
        }

        /// <summary>The student asked for help: counts as activity and resets the idle timer.</summary>
        public void NoteStudentActivity() => lastActivity = Now;
    }
}
