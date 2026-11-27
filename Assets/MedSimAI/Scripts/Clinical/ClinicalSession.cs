using System;
using System.Collections.Generic;
using System.Linq;

namespace MedSimAI.Clinical
{
    public enum ClinicalStage
    {
        Introduction, HistoryTaking, InitialAssessment, PhysicalExamination, Investigations, ClinicalReasoning, SupervisorFeedback, Debriefing
    }

    /// <summary>Controlled, flexible stage machine. Stages 1–4 (history..investigations) can be visited in any order.</summary>
    public class ClinicalStateMachine
    {
        public ClinicalStage Current { get; private set; } = ClinicalStage.Introduction;
        public event Action<ClinicalStage, ClinicalStage> StageChanged;

        static readonly ClinicalStage[] AssessmentStages =
            { ClinicalStage.HistoryTaking, ClinicalStage.InitialAssessment, ClinicalStage.PhysicalExamination, ClinicalStage.Investigations };

        public bool CanTransition(ClinicalStage to)
        {
            if (to == Current) return false;
            switch (Current)
            {
                case ClinicalStage.Introduction: return Array.IndexOf(AssessmentStages, to) >= 0;
                case ClinicalStage.HistoryTaking:
                case ClinicalStage.InitialAssessment:
                case ClinicalStage.PhysicalExamination:
                case ClinicalStage.Investigations:
                    return Array.IndexOf(AssessmentStages, to) >= 0 || to == ClinicalStage.ClinicalReasoning;
                case ClinicalStage.ClinicalReasoning:
                    return to == ClinicalStage.SupervisorFeedback || Array.IndexOf(AssessmentStages, to) >= 0;
                case ClinicalStage.SupervisorFeedback: return to == ClinicalStage.Debriefing;
                default: return false;
            }
        }

        public bool TryTransition(ClinicalStage to)
        {
            if (!CanTransition(to)) return false;
            var from = Current; Current = to; StageChanged?.Invoke(from, to); return true;
        }

        public void Reset() { Current = ClinicalStage.Introduction; }
    }

    /// <summary>
    /// Authoritative patient state. Only explicit scenario transitions can change it: the legacy event transitions and
    /// (v0.2.0) the authored condition state machine (patientStates + stateTransitions). The AI only ever reads it.
    /// </summary>
    public class PatientState
    {
        readonly ScenarioData scenario;
        Vitals current;
        readonly HashSet<string> appliedTransitions = new HashSet<string>();
        public IReadOnlyCollection<string> AppliedTransitions => appliedTransitions;
        public event Action<Vitals> VitalsChanged;
        /// <summary>(from, to, authored message)</summary>
        public event Action<PatientStateDef, PatientStateDef, string> ConditionChanged;
        public PatientStateDef Condition { get; private set; }
        public string ConditionId => Condition?.id;
        readonly List<string> conditionPath = new List<string>();
        public IReadOnlyList<string> ConditionPath => conditionPath;

        public PatientState(ScenarioData s) { scenario = s; Reset(); }
        public Vitals Vitals => current.Clone(); // defensive copy: callers cannot mutate

        /// <summary>Evaluates authored condition transitions. eventId == null → time-based check. Returns the authored message if the condition changed.</summary>
        internal string EvaluateConditionTransitions(string eventId, float now, Func<string, bool> has)
        {
            if (Condition == null || scenario.stateTransitions == null) return null;
            foreach (var t in scenario.stateTransitions)
            {
                if (t.trigger == null || t.from == null || Array.IndexOf(t.from, Condition.id) < 0 || t.to == Condition.id) continue;
                bool fire;
                if (t.trigger.type == "event") fire = eventId != null && eventId == t.trigger.eventId;
                else if (t.trigger.type == "time") fire = now >= t.trigger.afterSeconds && !(t.trigger.unlessEvents ?? new string[0]).Any(has);
                else fire = false;
                if (!fire) continue;
                var next = scenario.FindState(t.to); if (next == null) continue;
                var prev = Condition; Condition = next; conditionPath.Add(next.id);
                if (!next.inheritVitals && next.vitals != null) { current = next.vitals.Clone(); VitalsChanged?.Invoke(Vitals); }
                ConditionChanged?.Invoke(prev, next, t.message);
                return string.IsNullOrEmpty(t.message) ? "" : t.message;
            }
            return null;
        }

        internal string ApplyTransitionsFor(string eventId)
        {
            string message = null;
            foreach (var t in scenario.transitions ?? new Transition[0])
            {
                if (t.triggerEvent != eventId || appliedTransitions.Contains(t.id)) continue;
                appliedTransitions.Add(t.id);
                if (t.heartRate > 0) current.heartRate = t.heartRate;
                if (t.systolic > 0) current.systolic = t.systolic;
                if (t.diastolic > 0) current.diastolic = t.diastolic;
                if (t.spo2 > 0) current.spo2 = t.spo2;
                message = t.message;
                VitalsChanged?.Invoke(Vitals);
            }
            return message;
        }

        public void Reset()
        {
            appliedTransitions.Clear(); conditionPath.Clear();
            Condition = scenario.FindState(scenario.initialStateId);
            current = Condition != null && !Condition.inheritVitals && Condition.vitals != null ? Condition.vitals.Clone() : scenario.vitals.Clone();
            if (Condition != null) conditionPath.Add(Condition.id);
        }
    }

    public struct ClinicalActionEvent { public string eventId; public float time; public string detail; }

    public class ClinicalActionTracker
    {
        readonly List<ClinicalActionEvent> log = new List<ClinicalActionEvent>();
        public IReadOnlyList<ClinicalActionEvent> Log => log;
        public event Action<ClinicalActionEvent> Recorded;

        public bool Has(string eventId) => log.Any(e => e.eventId == eventId);
        public float? FirstTime(string eventId) { foreach (var e in log) if (e.eventId == eventId) return e.time; return null; }
        public int Count(string eventId) => log.Count(e => e.eventId == eventId);

        public void Record(string eventId, float time, string detail = null)
        {
            var e = new ClinicalActionEvent { eventId = eventId, time = time, detail = detail };
            log.Add(e); Recorded?.Invoke(e);
        }
        public void Reset() => log.Clear();
    }

    /// <summary>Deterministic utterance → scenario fact matching. Used for grading and offline answers (never the LLM).</summary>
    public static class HistoryMatcher
    {
        public static string Normalize(string s) => (s ?? "").ToLowerInvariant().Replace("’", "'");

        public static List<HistoryFact> Match(ScenarioData s, string utterance)
        {
            var u = Normalize(utterance);
            var result = new List<HistoryFact>();
            if (string.IsNullOrWhiteSpace(u)) return result;
            foreach (var f in s.historyFacts)
                if (f.keywords.Any(k => u.Contains(Normalize(k)))) result.Add(f);
            // Prefer clinically specific facts over generic greetings when both match.
            if (result.Count > 1) result = result.OrderByDescending(f => f.keyFact).ThenByDescending(f => BestKeywordLength(f, u)).ToList();
            return result;
        }

        static int BestKeywordLength(HistoryFact f, string u) => f.keywords.Where(k => u.Contains(Normalize(k))).Select(k => k.Length).DefaultIfEmpty(0).Max();

        public static Investigation MatchInvestigation(ScenarioData s, string utterance)
        {
            var u = Normalize(utterance);
            return s.investigations.FirstOrDefault(i => i.keywords.Any(k => u.Contains(Normalize(k))));
        }

        public static bool MentionsAny(string utterance, IEnumerable<string> keywords)
        {
            var u = Normalize(utterance);
            return keywords != null && keywords.Any(k => u.Contains(Normalize(k)));
        }
    }

    public struct ExamResult
    {
        public bool success; public string examId; public string finding; public string soundKey; public Vitals vitals; public string message;
    }

    public struct InvestigationResult
    {
        public bool success; public bool available; public string investigationId; public string name; public string result; public string imageKey;
    }

    /// <summary>
    /// Pure C# authoritative case state. ClinicalScenarioManager wraps this for Unity.
    /// All instrument readings and results are read from here — deterministic for the same inputs.
    /// </summary>
    public class ClinicalSession
    {
        public ScenarioData Scenario { get; }
        public ClinicalStateMachine Stages { get; } = new ClinicalStateMachine();
        public PatientState Patient { get; }
        public ClinicalActionTracker Tracker { get; } = new ClinicalActionTracker();
        public Func<float> Clock { get; set; } = () => 0f;
        public int HintsRequested { get; private set; }
        readonly Dictionary<string, int> hintCursor = new Dictionary<string, int>();
        public event Action<string> SystemMessage;

        public ClinicalSession(ScenarioData scenario)
        {
            var errors = ScenarioValidator.Validate(scenario);
            if (errors.Count > 0) throw new ArgumentException("Invalid scenario: " + string.Join("; ", errors));
            Scenario = scenario;
            Patient = new PatientState(scenario);
        }

        float Now => Clock();

        public void Record(string eventId, string detail = null)
        {
            Tracker.Record(eventId, Now, detail);
            var msg = Patient.ApplyTransitionsFor(eventId);
            if (msg != null) SystemMessage?.Invoke(msg);
            CheckCondition(eventId);
        }

        void CheckCondition(string eventId)
        {
            var before = Patient.ConditionId;
            var m = Patient.EvaluateConditionTransitions(eventId, Now, Tracker.Has);
            if (m != null)
            {
                Tracker.Record(ClinicalEvents.State(Patient.ConditionId), Now, before);
                if (m.Length > 0) SystemMessage?.Invoke(m);
            }
        }

        /// <summary>Periodic update (time-based authored transitions). Called by ClinicalScenarioManager ~2x per second, never per frame AI.</summary>
        public void Tick() { if (Stages.Current != ClinicalStage.Debriefing && Stages.Current != ClinicalStage.SupervisorFeedback) CheckCondition(null); }

        /// <summary>Analysis of the last statement made to the supervisor (deterministic).</summary>
        public StatementAnalysis.Result LastStatement { get; private set; }

        void EnsureAssessmentStage(ClinicalStage preferred)
        {
            if (Stages.Current == ClinicalStage.Introduction || Stages.Current == preferred) { Stages.TryTransition(preferred); return; }
            if (Stages.CanTransition(preferred) && Stages.Current != ClinicalStage.SupervisorFeedback && Stages.Current != ClinicalStage.Debriefing)
                Stages.TryTransition(preferred);
        }

        /// <summary>Records which history topics the student's utterance covered. Returns matched facts (patient-visible answers).</summary>
        public List<HistoryFact> ProcessStudentQuestion(string utterance)
        {
            var matched = HistoryMatcher.Match(Scenario, utterance);
            if (matched.Count > 0 && (Stages.Current == ClinicalStage.Introduction || IsAssessment(Stages.Current)))
                EnsureAssessmentStage(ClinicalStage.HistoryTaking);
            foreach (var f in matched)
                if (!Tracker.Has(ClinicalEvents.Fact(f.id))) Record(ClinicalEvents.Fact(f.id), utterance);
            return matched;
        }

        /// <summary>Student speaking to the supervisor: detects investigation requests and escalation deterministically.</summary>
        public InvestigationResult? ProcessInstructorStatement(string utterance)
        {
            InvestigationResult? inv = null;
            var a = StatementAnalysis.Analyse(Scenario, utterance);
            LastStatement = a;
            bool reasoning = Stages.Current == ClinicalStage.ClinicalReasoning;
            var match = HistoryMatcher.MatchInvestigation(Scenario, utterance);
            // Mentioning an investigation that was already done ("the ECG shows…") is reasoning, not a new request.
            if (match != null && !(a.isQuestion && a.diagnosticQuestion) && !Tracker.Has(ClinicalEvents.Inv(match.id)))
            {
                inv = RequestInvestigation(match.id);
                if (reasoning) Stages.TryTransition(ClinicalStage.ClinicalReasoning); // a new request during the discussion keeps the discussion stage
            }
            if (Stages.Current != ClinicalStage.Introduction && a.escalationRequested) Escalate(utterance);
            if (a.diagnosisNamed && !Tracker.Has(ClinicalEvents.DiagnosisNamed)) Record(ClinicalEvents.DiagnosisNamed, utterance);
            if (a.diagnosticQuestion) Record(ClinicalEvents.DiagnosticQuestion, utterance);
            if (Stages.Current == ClinicalStage.ClinicalReasoning) RecordReasoning(utterance, a.concepts, false);
            return inv;
        }

        public static bool IsAssessment(ClinicalStage s) =>
            s == ClinicalStage.HistoryTaking || s == ClinicalStage.InitialAssessment || s == ClinicalStage.PhysicalExamination || s == ClinicalStage.Investigations;

        public ExamResult PerformExam(string instrument, string zoneId)
        {
            var exam = Scenario.examFindings.FirstOrDefault(e => e.instrument == instrument && e.zoneIds.Contains(zoneId));
            if (exam == null) return new ExamResult { success = false, message = "This instrument cannot be used at that location." };
            EnsureAssessmentStage(instrument == "stethoscope" ? ClinicalStage.PhysicalExamination : ClinicalStage.InitialAssessment);
            Record(ClinicalEvents.Exam(exam.id), zoneId);
            return new ExamResult { success = true, examId = exam.id, finding = exam.finding, soundKey = exam.soundKey, vitals = Patient.Vitals };
        }

        public InvestigationResult RequestInvestigation(string investigationId)
        {
            var inv = Scenario.FindInvestigation(investigationId);
            if (inv == null) return new InvestigationResult { success = false, available = false, investigationId = investigationId, result = "This investigation is not part of this scenario." };
            if (!inv.available) return new InvestigationResult { success = true, available = false, investigationId = inv.id, name = inv.name, result = inv.result };
            EnsureAssessmentStage(ClinicalStage.Investigations);
            if (!Tracker.Has(ClinicalEvents.Inv(inv.id))) Record(ClinicalEvents.Inv(inv.id));
            return new InvestigationResult { success = true, available = true, investigationId = inv.id, name = inv.name, result = inv.result, imageKey = inv.imageKey };
        }

        public string RequestHint()
        {
            HintsRequested++;
            Record(ClinicalEvents.HintRequested, Stages.Current.ToString());
            var stage = Stages.Current.ToString();
            var list = Scenario.hints.Where(h => h.stage == stage).OrderBy(h => h.order).ToList();
            if (list.Count == 0)
            {
                // Fall back to the most relevant incomplete area.
                var next = SuggestFocusStage();
                list = Scenario.hints.Where(h => h.stage == next.ToString()).OrderBy(h => h.order).ToList();
                stage = next.ToString();
            }
            if (list.Count == 0) return Scenario.instructorLines.noHintAvailable;
            hintCursor.TryGetValue(stage, out var idx);
            var hint = list[Math.Min(idx, list.Count - 1)];
            hintCursor[stage] = idx + 1;
            return hint.text;
        }

        public ClinicalStage SuggestFocusStage()
        {
            bool keyHistory = Scenario.historyFacts.Where(f => f.keyFact).Count(f => Tracker.Has(ClinicalEvents.Fact(f.id))) >= 6;
            if (!keyHistory) return ClinicalStage.HistoryTaking;
            if (!Tracker.Has(ClinicalEvents.Exam("EX_BP")) || !Tracker.Has(ClinicalEvents.Exam("EX_SPO2")) || !Tracker.Has(ClinicalEvents.Exam("EX_AUSC_HEART")))
                return ClinicalStage.PhysicalExamination;
            if (!Tracker.Has(ClinicalEvents.Inv("INV_ECG"))) return ClinicalStage.Investigations;
            return ClinicalStage.ClinicalReasoning;
        }

        /// <summary>Student summary to the supervisor. Records SUMMARY_GIVEN and detects escalation intent deterministically.</summary>
        public bool SubmitReasoning(string summary)
        {
            if (Stages.Current != ClinicalStage.ClinicalReasoning) Stages.TryTransition(ClinicalStage.ClinicalReasoning);
            if (Stages.Current != ClinicalStage.ClinicalReasoning) return false;
            var a = StatementAnalysis.Analyse(Scenario, summary);
            LastStatement = a;
            if (a.escalationRequested) Escalate(summary);
            if (a.diagnosisNamed && !Tracker.Has(ClinicalEvents.DiagnosisNamed)) Record(ClinicalEvents.DiagnosisNamed, summary);
            RecordReasoning(summary, a.concepts, false);
            return true;
        }

        public const string AiTag = "AI_INTENT:";

        /// <summary>
        /// Records reasoning concepts. Deterministic matches are authoritative. AI-only matches are recorded with the AI tag
        /// (shown as "AI-interpreted — for instructor review"). SUMMARY_GIVEN requires at least two DETERMINISTIC concepts.
        /// </summary>
        public void RecordReasoning(string utterance, IEnumerable<string> concepts, bool viaAI)
        {
            if (Scenario.reasoningConcepts == null) { if (!Tracker.Has(ClinicalEvents.SummaryGiven)) Record(ClinicalEvents.SummaryGiven, utterance); return; }
            Record(ClinicalEvents.ReasoningGiven, (viaAI ? AiTag : "") + utterance);
            foreach (var id in concepts ?? Enumerable.Empty<string>())
                if (Scenario.FindConcept(id) != null && !Tracker.Has(ClinicalEvents.Concept(id))) Record(ClinicalEvents.Concept(id), (viaAI ? AiTag : "") + utterance);
            int deterministic = Tracker.Log.Where(e => e.eventId.StartsWith("CONCEPT:") && (e.detail == null || !e.detail.StartsWith(AiTag))).Select(e => e.eventId).Distinct().Count();
            if (deterministic >= 2 && !Tracker.Has(ClinicalEvents.SummaryGiven)) Record(ClinicalEvents.SummaryGiven, utterance);
        }

        public void Escalate(string detail = "button") { if (!Tracker.Has(ClinicalEvents.EscalationRequested)) Record(ClinicalEvents.EscalationRequested, detail); }

        public bool AdvanceToFeedback()
        {
            if (Stages.Current != ClinicalStage.ClinicalReasoning && !Stages.TryTransition(ClinicalStage.ClinicalReasoning)) return false;
            return Stages.TryTransition(ClinicalStage.SupervisorFeedback);
        }

        public bool AdvanceToDebrief()
        {
            if (Stages.Current == ClinicalStage.SupervisorFeedback || AdvanceToFeedback())
            {
                if (Stages.TryTransition(ClinicalStage.Debriefing)) { Record(ClinicalEvents.ScenarioCompleted); return true; }
            }
            return false;
        }

        public void Reset()
        {
            Stages.Reset(); Patient.Reset(); Tracker.Reset(); HintsRequested = 0; hintCursor.Clear();
        }
    }
}
