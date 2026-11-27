using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Clinical
{
    // Authoritative, versioned scenario schema. Loaded from JSON (Resources/Scenarios/<id>.json).
    // The generative AI never writes to these objects; it only receives filtered read-only views.

    [Serializable] public class ReviewInfo
    {
        public string status = "DRAFT";         // DRAFT | IN_REVIEW | APPROVED
        public string reviewerName;
        public string reviewerCredentials;
        public string reviewDate;
        public string notes;
    }

    [Serializable] public class Objective { public string id; public string text; }

    [Serializable] public class PatientInfo
    {
        public string displayName; public int age; public string sex; public string occupation; public string fictionalNotice;
    }

    [Serializable] public class Vitals
    {
        public int heartRate; public int systolic; public int diastolic; public int spo2; public int respiratoryRate; public float temperatureC; public string rhythm;
        public Vitals Clone() => (Vitals)MemberwiseClone();
    }

    [Serializable] public class HistoryFact
    {
        public string id; public string category; public string[] keywords; public string answer; public bool keyFact;
    }

    [Serializable] public class ExamFinding
    {
        public string id; public string[] zoneIds; public string instrument; public string finding; public string soundKey;
    }

    [Serializable] public class Investigation
    {
        public string id; public string name; public string[] keywords; public bool available; public string result; public string imageKey;
    }

    [Serializable] public class Transition
    {
        public string id; public string triggerEvent; public string message;
        public int heartRate, systolic, diastolic, spo2; // 0 = unchanged
    }

    [Serializable] public class RubricItem
    {
        public string id; public string objectiveId; public string eventId; public int points; public string category; public bool critical; public string description;
    }

    [Serializable] public class TimedExpectation { public string eventId; public float withinSeconds; public string explanation; }

    [Serializable] public class Hint { public string stage; public int order; public string text; }

    [Serializable] public class InstructorLines
    {
        public string welcome, tutorial, historyDone, examDone, ecgShown, reasoningPrompt, escalationAck, debriefIntro, outOfScope, noHintAvailable, onboardingRole, onboardingInstruments;
        public string welcomeBrief, roomEntry, discussionIntro;
    }

    [Serializable] public class ExpectedReasoning
    {
        public string workingDiagnosis; public string[] keyPoints; public string[] reasoningKeywords;
    }

    [Serializable] public class Reference { public string id; public string citation; public string url; public string usedFor; }

    // ---------------------------------------------------------------- v0.2.0 (DRAFT, requires clinical review)
    [Serializable] public class PersonaBrief { public string summary, speechStyle, boundaries; }
    [Serializable] public class Personas { public PersonaBrief patient, instructor; }

    /// <summary>An authored patient condition. The AI receives it read-only; only authored transitions change it.</summary>
    [Serializable] public class PatientStateDef
    {
        public string id, label; public bool inheritVitals; public Vitals vitals; public int painScore;
        public string expression, breathing, posture; public string[] statements; public string context;
    }
    [Serializable] public class StateTrigger { public string type; public float afterSeconds; public string eventId; public string[] unlessEvents; }
    [Serializable] public class StateTransition { public string id; public string[] from; public string to; public StateTrigger trigger; public string message; }
    [Serializable] public class InstrumentGuide { public string instrument, hint; }

    [Serializable] public class InterventionTrigger
    {
        public string type;          // event | missing | state | stage | idle
        public string eventId; public int count = 1; public float withinSeconds; public float afterSeconds; public string sinceEvent;
        public string stage; public string stateId; public string[] requireEvents; public string[] unlessEvents; public bool unlessAll;
    }
    /// <summary>Scenario-authored supervisor intervention rule (see INTELLIGENT_SUPERVISOR.md).</summary>
    [Serializable] public class InterventionRule
    {
        public string id; public string severity; public int priority; public InterventionTrigger trigger;
        public float graceSeconds, cooldownSeconds; public int maxRepeats = 1; public float repeatAfterSeconds;
        public string[] cancelEvents; public string focus; public string[] lines; public string purpose;
        public bool IsSafety => severity == "safety";
    }
    [Serializable] public class SupervisorPolicy { public float globalCooldownSeconds = 20, tickSeconds = 0.5f; public bool silentOnCorrectActions = true, ordinaryNeedsStudentIdle = true; }
    [Serializable] public class EscalationLexicon { public string[] explicit_ = new string[0]; public string[] actions, targets; }
    [Serializable] public class ReasoningConcept { public string id, label; public string[] anyOf, alsoAnyOf; }
    [Serializable] public class DiscussionQuestion { public string id, question; public string[] concepts; }

    [Serializable] public class ScenarioData
    {
        public string scenarioId; public string version; public string title; public string language;
        public ReviewInfo review;
        public Objective[] objectives;
        public PatientInfo patient;
        public string chiefComplaint;
        public Vitals vitals;
        public HistoryFact[] historyFacts;
        public string[] unknownAnswers;
        public ExamFinding[] examFindings;
        public Investigation[] investigations;
        public Transition[] transitions;
        public string[] escalationKeywords;
        public RubricItem[] rubric;
        public TimedExpectation[] timedExpectations;
        public Hint[] hints;
        public InstructorLines instructorLines;
        public ExpectedReasoning expectedReasoning;
        public Reference[] references;
        // v0.2.0
        public Personas personas;
        public PatientStateDef[] patientStates;
        public string initialStateId;
        public StateTransition[] stateTransitions;
        public InstrumentGuide[] instrumentGuides;
        public InterventionRule[] interventions;
        public SupervisorPolicy supervisorPolicy;
        public EscalationLexicon escalation;
        public string[] diagnosisKeywords;
        public ReasoningConcept[] reasoningConcepts;
        public DiscussionQuestion[] discussion;

        public PatientStateDef FindState(string id) => patientStates == null ? null : Array.Find(patientStates, x => x.id == id);
        public ReasoningConcept FindConcept(string id) => reasoningConcepts == null ? null : Array.Find(reasoningConcepts, x => x.id == id);

        public bool IsClinicallyValidated =>
            review != null && review.status == "APPROVED" &&
            !string.IsNullOrWhiteSpace(review.reviewerName) &&
            !string.IsNullOrWhiteSpace(review.reviewerCredentials) &&
            !string.IsNullOrWhiteSpace(review.reviewDate);

        public string ValidationBanner => IsClinicallyValidated
            ? $"Clinically reviewed by {review.reviewerName} ({review.reviewerCredentials}) on {review.reviewDate}"
            : "DRAFT — NOT CLINICALLY VALIDATED · Technical demonstration only · Fictional patient";

        // "explicit" is a C# keyword: the JSON field is mapped to explicit_ before parsing.
        public static ScenarioData FromJson(string json) => JsonUtility.FromJson<ScenarioData>(json.Replace("\"explicit\":", "\"explicit_\":"));

        public static ScenarioData LoadFromResources(string scenarioId)
        {
            var ta = Resources.Load<TextAsset>("Scenarios/" + scenarioId);
            if (ta == null) throw new InvalidOperationException("Scenario not found: " + scenarioId);
            return FromJson(ta.text);
        }

        public HistoryFact FindFact(string id) => Array.Find(historyFacts, f => f.id == id);
        public ExamFinding FindExam(string id) => Array.Find(examFindings, f => f.id == id);
        public Investigation FindInvestigation(string id) => Array.Find(investigations, f => f.id == id);
    }

    public static class ScenarioValidator
    {
        // Returns a list of integrity problems; empty means the scenario is structurally valid.
        public static List<string> Validate(ScenarioData s)
        {
            var errors = new List<string>();
            if (s == null) { errors.Add("Scenario is null"); return errors; }
            if (string.IsNullOrEmpty(s.scenarioId)) errors.Add("Missing scenarioId");
            if (string.IsNullOrEmpty(s.version)) errors.Add("Missing version");
            if (s.review == null) errors.Add("Missing review block");
            if (s.vitals == null) errors.Add("Missing vitals");
            else
            {
                if (s.vitals.heartRate < 20 || s.vitals.heartRate > 250) errors.Add("Heart rate out of plausible range");
                if (s.vitals.systolic <= s.vitals.diastolic) errors.Add("Systolic must exceed diastolic");
                if (s.vitals.spo2 < 50 || s.vitals.spo2 > 100) errors.Add("SpO2 out of range");
            }
            var ids = new HashSet<string>();
            void Unique(string id, string kind) { if (string.IsNullOrEmpty(id) || !ids.Add(id)) errors.Add($"Duplicate or empty {kind} id: {id}"); }
            foreach (var f in s.historyFacts ?? new HistoryFact[0]) { Unique(f.id, "fact"); if (f.keywords == null || f.keywords.Length == 0) errors.Add("Fact without keywords: " + f.id); if (string.IsNullOrEmpty(f.answer)) errors.Add("Fact without answer: " + f.id); }
            foreach (var e in s.examFindings ?? new ExamFinding[0]) Unique(e.id, "exam");
            foreach (var i in s.investigations ?? new Investigation[0]) Unique(i.id, "investigation");
            var objIds = new HashSet<string>();
            foreach (var o in s.objectives ?? new Objective[0]) objIds.Add(o.id);
            foreach (var r in s.rubric ?? new RubricItem[0])
            {
                Unique(r.id, "rubric");
                if (!objIds.Contains(r.objectiveId)) errors.Add($"Rubric {r.id} references unknown objective {r.objectiveId}");
                if (!EventExists(s, r.eventId)) errors.Add($"Rubric {r.id} references unknown event {r.eventId}");
                if (r.category != "observable" && r.category != "communication") errors.Add($"Rubric {r.id} has invalid category");
            }
            if (s.unknownAnswers == null || s.unknownAnswers.Length == 0) errors.Add("No unknown-answer fallbacks");
            if (s.references == null || s.references.Length == 0) errors.Add("No references");
            if (s.instructorLines == null) errors.Add("Missing instructor lines");
            // v0.2.0 dynamic states, interventions, concepts
            var stateIds = new HashSet<string>();
            foreach (var st in s.patientStates ?? new PatientStateDef[0])
            {
                if (string.IsNullOrEmpty(st.id) || !stateIds.Add(st.id)) errors.Add("Duplicate or empty state id: " + st.id);
                if (!st.inheritVitals && (st.vitals == null || st.vitals.systolic <= st.vitals.diastolic || st.vitals.heartRate < 20 || st.vitals.spo2 < 50 || st.vitals.spo2 > 100)) errors.Add("State " + st.id + " has implausible vitals");
            }
            if (stateIds.Count > 0 && !stateIds.Contains(s.initialStateId)) errors.Add("initialStateId not found: " + s.initialStateId);
            foreach (var t in s.stateTransitions ?? new StateTransition[0])
            {
                if (!stateIds.Contains(t.to)) errors.Add($"Transition {t.id} targets unknown state {t.to}");
                foreach (var f in t.from ?? new string[0]) if (!stateIds.Contains(f)) errors.Add($"Transition {t.id} from unknown state {f}");
                if (t.trigger == null || (t.trigger.type != "time" && t.trigger.type != "event")) errors.Add($"Transition {t.id} has an invalid trigger");
            }
            var ruleIds = new HashSet<string>();
            foreach (var r in s.interventions ?? new InterventionRule[0])
            {
                if (string.IsNullOrEmpty(r.id) || !ruleIds.Add(r.id)) errors.Add("Duplicate or empty intervention id: " + r.id);
                if (r.lines == null || r.lines.Length == 0) errors.Add($"Intervention {r.id} has no approved lines");
                if (r.trigger == null || Array.IndexOf(new[] { "event", "missing", "state", "stage", "idle" }, r.trigger.type) < 0) errors.Add($"Intervention {r.id} has an invalid trigger");
                if (r.trigger != null && r.trigger.type == "state" && !stateIds.Contains(r.trigger.stateId)) errors.Add($"Intervention {r.id} references unknown state {r.trigger.stateId}");
            }
            var conceptIds = new HashSet<string>();
            foreach (var c in s.reasoningConcepts ?? new ReasoningConcept[0]) { if (string.IsNullOrEmpty(c.id) || !conceptIds.Add(c.id)) errors.Add("Duplicate concept " + c.id); if (c.anyOf == null || c.anyOf.Length == 0) errors.Add("Concept without keywords " + c.id); }
            foreach (var q in s.discussion ?? new DiscussionQuestion[0]) foreach (var c in q.concepts ?? new string[0]) if (!conceptIds.Contains(c)) errors.Add($"Discussion {q.id} references unknown concept {c}");
            return errors;
        }

        static bool EventExists(ScenarioData s, string eventId)
        {
            if (eventId.StartsWith("FACT:")) return s.FindFact(eventId.Substring(5)) != null;
            if (eventId.StartsWith("EXAM:")) return s.FindExam(eventId.Substring(5)) != null;
            if (eventId.StartsWith("INV:")) return s.FindInvestigation(eventId.Substring(4)) != null;
            if (eventId.StartsWith("CONCEPT:")) return s.FindConcept(eventId.Substring(8)) != null;
            return Array.IndexOf(ClinicalEvents.Named, eventId) >= 0;
        }
    }

    public static class ClinicalEvents
    {
        public const string EscalationRequested = "ESCALATION_REQUESTED";
        public const string EcgReviewed = "ECG_REVIEWED";
        public const string SummaryGiven = "SUMMARY_GIVEN";
        public const string HintRequested = "HINT_REQUESTED";
        public const string HandsSanitized = "HANDS_SANITIZED";
        public const string ScenarioCompleted = "SCENARIO_COMPLETED";
        // v0.2.0 observation events (feed the SupervisorDirector; most are not scored)
        public const string DoorOpened = "DOOR_OPENED";
        public const string EnteredRoom = "ENTERED_ROOM";
        public const string InstrumentPicked = "INSTRUMENT_PICKED";
        public const string InstrumentOnPatient = "INSTRUMENT_ON_PATIENT";
        public const string ZoneRejected = "ZONE_REJECTED";
        public const string InstrumentRemovedEarly = "INSTRUMENT_REMOVED_EARLY";
        public const string DiagnosisNamed = "DIAGNOSIS_NAMED";
        public const string DiagnosticQuestion = "DIAGNOSTIC_QUESTION";
        public const string ReasoningGiven = "REASONING_GIVEN";
        public const string StudentSpoke = "STUDENT_SPOKE";
        public static readonly string[] Named = { EscalationRequested, EcgReviewed, SummaryGiven, HintRequested, HandsSanitized, ScenarioCompleted,
            DoorOpened, EnteredRoom, InstrumentPicked, InstrumentOnPatient, ZoneRejected, InstrumentRemovedEarly, DiagnosisNamed, DiagnosticQuestion, ReasoningGiven, StudentSpoke };
        public static string Concept(string id) => "CONCEPT:" + id;
        public static string State(string id) => "STATE:" + id;
        public static string Intervention(string id) => "INTERVENTION:" + id;
        public static string Fact(string id) => "FACT:" + id;
        public static string Exam(string id) => "EXAM:" + id;
        public static string Inv(string id) => "INV:" + id;
    }
}
