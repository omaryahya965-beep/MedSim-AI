using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;

namespace MedSimAI.Surgery.AI
{
    /// <summary>What the supervising surgeon should say (approved text; the AI may only rephrase it under grounding).</summary>
    [Serializable]
    public class SupervisorUtterance
    {
        public string ruleId = "", kind = "", objectiveId = "", reason = "";
        public string line = "";          // approved rule line in the session language
        public string hint = "";          // approved objective hint appended (if the rule gives one)
        public string lang = "en";
        public bool countsAsHint, isIntervention;
        public string Text => string.IsNullOrEmpty(hint) ? line : (line + " " + hint).Trim();
        public override string ToString() => $"[{kind}:{ruleId}] {Text}";
    }

    /// <summary>
    /// Deterministic intelligent surgical supervisor. Observes ONLY structured events from the bus, applies the authored
    /// rules (grace, priority, cooldown, max repeats, hint ladder), stays silent on correct work and lets the student
    /// self-correct. It never decides surgical correctness: mechanics do. Engine-free and fully testable.
    /// </summary>
    public class SurgicalSupervisor
    {
        class Pending { public SupervisorRule rule; public double due; public string objectiveId, reason; public bool fromRejection; }

        readonly SurgicalScenarioData s;
        readonly ISurgeryEventSink sink;
        readonly Func<double> now;
        readonly List<Pending> pending = new List<Pending>();
        readonly Dictionary<string, int> fired = new Dictionary<string, int>();
        readonly Dictionary<string, double> lastFired = new Dictionary<string, double>();
        readonly Dictionary<string, int> hintIndex = new Dictionary<string, int>();
        readonly HashSet<string> idleFiredSinceActivity = new HashSet<string>();
        double lastSpoke = double.NegativeInfinity, lastActivity;
        int rejectionsInObjective;
        bool active;

        public string Language = "en";
        public string CurrentObjectiveId { get; private set; } = "";
        public int HintsGiven { get; private set; }
        public int Interventions { get; private set; }
        public int SelfCorrections { get; private set; }
        public event Action<SupervisorUtterance> Speak;

        public SurgicalSupervisor(SurgicalScenarioData scenario, ISurgeryEventSink sink, Func<double> clock)
        { s = scenario ?? throw new ArgumentNullException(nameof(scenario)); this.sink = sink; now = clock ?? (() => 0); }

        public void Reset()
        {
            pending.Clear(); fired.Clear(); lastFired.Clear(); hintIndex.Clear(); idleFiredSinceActivity.Clear();
            lastSpoke = double.NegativeInfinity; rejectionsInObjective = 0; CurrentObjectiveId = ""; HintsGiven = 0; Interventions = 0; SelfCorrections = 0; active = false;
        }

        /// <summary>Feed every bus event here (subscribe to SurgeryEventBus.EventPublished).</summary>
        public void OnEvent(SurgeryEvent e)
        {
            if (e == null) return;
            bool fromMechanics = e.source == SurgeryEventSources.Mechanics || e.source == SurgeryEventSources.Mock;
            switch (e.id)
            {
                case SurgeryEventIds.SessionStarted: active = true; lastActivity = now(); Trigger("session_started", "", ""); break;
                case SurgeryEventIds.SessionReset: Reset(); break;
                case SurgeryEventIds.SessionCompleted: Trigger("session_completed", CurrentObjectiveId, ""); break;
                case SurgeryEventIds.HelpRequested: Trigger("help_requested", CurrentObjectiveId, ""); break;
            }
            if (!fromMechanics || !SurgeryEventIds.IsGameplay(e.id)) return;

            lastActivity = now(); idleFiredSinceActivity.Clear();
            switch (e.id)
            {
                case SurgeryEventIds.ObjectiveStarted:
                    CurrentObjectiveId = e.objectiveId; rejectionsInObjective = 0;
                    Trigger("objective_started", e.objectiveId, ""); break;
                case SurgeryEventIds.InteractionRejected:
                    rejectionsInObjective++;
                    Trigger("rejected", Obj(e), e.reason, true);
                    foreach (var r in s.supervisorRules.Where(r => r.trigger.type == "repeated_rejection" && rejectionsInObjective == Math.Max(1, r.trigger.count)))
                        Enqueue(r, Obj(e), e.reason, true);
                    break;
                case SurgeryEventIds.AnatomyInteracted:
                case SurgeryEventIds.AnatomyStateChanged:
                case SurgeryEventIds.ImplantPlacementCompleted:
                case SurgeryEventIds.ObjectiveCompleted:
                    // Correct progress: the student self-corrected, so drop still-pending (non-safety) correction lines.
                    int before = pending.Count;
                    pending.RemoveAll(p => p.fromRejection && p.rule.kind != "safety");
                    if (pending.Count < before) SelfCorrections++;
                    if (e.id == SurgeryEventIds.ObjectiveCompleted) { rejectionsInObjective = 0; Trigger("objective_completed", e.objectiveId, ""); }
                    break;
            }
        }

        /// <summary>Call regularly (e.g. every 0.25–0.5 s). Speaks at most one line per tick.</summary>
        public SupervisorUtterance Tick()
        {
            if (!active) return null;
            double t = now();
            if (!string.IsNullOrEmpty(CurrentObjectiveId))
                foreach (var r in s.supervisorRules.Where(r => r.trigger.type == "idle" && r.trigger.seconds > 0))
                    if (t - lastActivity >= r.trigger.seconds && idleFiredSinceActivity.Add(r.id)) Enqueue(r, CurrentObjectiveId, "", false);

            var due = pending.Where(p => p.due <= t).OrderByDescending(p => p.rule.priority).ThenBy(p => p.due).ToList();
            foreach (var p in due)
            {
                if (!Allowed(p.rule, t)) { pending.Remove(p); continue; }
                pending.Remove(p);
                return Emit(p, t);
            }
            return null;
        }

        /// <summary>Approved explanation of the current objective (student asked "what is required?"). Not counted as a hint.</summary>
        public string ExplainCurrentObjective(string lang = null)
        {
            var o = s.Objective(CurrentObjectiveId); lang ??= Language;
            if (o == null) return lang == "ar" ? "ما في خطوة فعّالة هلأ." : "There is no active step right now.";
            return o.title.Get(lang) + ". " + o.explanation.Get(lang);
        }

        bool Allowed(SupervisorRule r, double t)
        {
            fired.TryGetValue(r.id, out var n);
            if (n >= Math.Max(1, r.maxTimes)) return false;
            if (lastFired.TryGetValue(r.id, out var lf) && t - lf < r.cooldownSeconds) return false;
            bool bypassGlobal = r.kind == "safety" || r.kind == "welcome" || r.trigger.type == "help_requested" || r.kind == "praise";
            if (!bypassGlobal && t - lastSpoke < s.supervisorPolicy.globalCooldownSeconds) return false;
            return true;
        }

        SupervisorUtterance Emit(Pending p, double t)
        {
            var r = p.rule;
            fired[r.id] = (fired.TryGetValue(r.id, out var n) ? n : 0) + 1; lastFired[r.id] = t; lastSpoke = t;
            var u = new SupervisorUtterance { ruleId = r.id, kind = r.kind, objectiveId = p.objectiveId ?? "", reason = p.reason ?? "", lang = Language, line = r.line.Get(Language) };
            if (r.givesHint) { u.hint = NextHint(p.objectiveId); u.countsAsHint = !string.IsNullOrEmpty(u.hint); }
            u.isIntervention = r.kind == "intervention" || r.kind == "safety";
            if (u.countsAsHint) { HintsGiven++; sink?.Publish(new SurgeryEvent(SurgeryEventIds.HintGiven, SurgeryEventSources.AI) { objectiveId = u.objectiveId, reason = r.id, characterId = SurgeryCharacterIds.Surgeon }); }
            if (u.isIntervention) { Interventions++; sink?.Publish(new SurgeryEvent(SurgeryEventIds.SupervisorIntervened, SurgeryEventSources.AI) { objectiveId = u.objectiveId, reason = r.kind + ":" + r.id, characterId = SurgeryCharacterIds.Surgeon }); }
            Speak?.Invoke(u);
            return u;
        }

        string NextHint(string objectiveId)
        {
            var o = s.Objective(string.IsNullOrEmpty(objectiveId) ? CurrentObjectiveId : objectiveId);
            if (o == null || o.hints.Length == 0) return "";
            hintIndex.TryGetValue(o.id, out var i);
            int cap = Math.Min(o.hints.Length, Math.Max(1, s.supervisorPolicy.maxHintsPerObjective));
            var h = o.hints[Math.Min(i, cap - 1)];      // least specific first; after the cap, repeat the most specific allowed
            hintIndex[o.id] = i + 1;
            return h.Get(Language);
        }

        void Trigger(string type, string objectiveId, string reason, bool fromRejection = false)
        {
            foreach (var r in s.supervisorRules)
            {
                var tr = r.trigger;
                if (tr.type != type) continue;
                if (!string.IsNullOrEmpty(tr.objectiveId) && tr.objectiveId != objectiveId) continue;
                if (type == "rejected" && !string.IsNullOrEmpty(tr.reason) && tr.reason != reason) continue;
                Enqueue(r, objectiveId, reason, fromRejection);
            }
        }

        void Enqueue(SupervisorRule r, string objectiveId, string reason, bool fromRejection)
        {
            if (pending.Any(p => p.rule.id == r.id)) return;   // one pending instance per rule
            pending.Add(new Pending { rule = r, due = now() + Math.Max(0, r.graceSeconds), objectiveId = objectiveId, reason = reason, fromRejection = fromRejection });
        }

        string Obj(SurgeryEvent e) => string.IsNullOrEmpty(e.objectiveId) ? CurrentObjectiveId : e.objectiveId;
    }
}
