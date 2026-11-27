using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.Clinical;
using UnityEngine;

namespace MedSimAI.Assessment
{
    [Serializable] public class CriterionResult
    {
        public string id; public string objectiveId; public string description; public string category; public bool critical; public bool met; public int points; public int maxPoints; public float time = -1;
        public bool viaAIIntent; // met only because the AI classified the student's question (transparent, labelled in debrief)
    }

    [Serializable] public class ObjectiveResult { public string id; public string text; public int met; public int total; public bool complete; }

    [Serializable] public class TimedResult { public string eventId; public bool met; public float actualSeconds = -1; public float withinSeconds; public string explanation; }

    [Serializable] public class AssessmentResult
    {
        public string scenarioId; public string scenarioVersion; public string reviewStatus; public string sessionStartUtc; public float durationSeconds;
        public int observableScore, observableMax, communicationScore, communicationMax;
        public int hintsRequested;
        public bool escalated;
        public List<CriterionResult> criteria = new List<CriterionResult>();
        public List<ObjectiveResult> objectives = new List<ObjectiveResult>();
        public List<TimedResult> timed = new List<TimedResult>();
        public List<string> missedCritical = new List<string>();
        public string disclaimer;
        public string aiCommentary; // optional, clearly labelled, never affects scores
        public string aiCommentaryLabel = "AI-assisted commentary (unvalidated, does not affect score)";
        public float ObservablePercent => observableMax == 0 ? 0 : 100f * observableScore / observableMax;
        public float CommunicationPercent => communicationMax == 0 ? 0 : 100f * communicationScore / communicationMax;
    }

    /// <summary>Deterministic rubric scoring. Same event log → same result. Speed is never rewarded beyond safety-relevant time targets.</summary>
    public static class AssessmentEngine
    {
        public static AssessmentResult Evaluate(ScenarioData s, IReadOnlyList<ClinicalActionEvent> log, int hintsRequested, float durationSeconds, string startUtc = null)
        {
            var r = new AssessmentResult
            {
                scenarioId = s.scenarioId, scenarioVersion = s.version, reviewStatus = s.review?.status, durationSeconds = durationSeconds,
                hintsRequested = hintsRequested, sessionStartUtc = startUtc ?? DateTime.UtcNow.ToString("o"),
                disclaimer = "Prototype educational feedback. Not a clinical competency certification." + (s.IsClinicallyValidated ? "" : " Scenario is DRAFT — NOT CLINICALLY VALIDATED.")
            };
            float? First(string id) { foreach (var e in log) if (e.eventId == id) return e.time; return null; }
            bool ViaAI(string id) { foreach (var e in log) if (e.eventId == id) return e.detail != null && e.detail.StartsWith("AI_INTENT:"); return false; }

            foreach (var item in s.rubric)
            {
                var t = First(item.eventId);
                var c = new CriterionResult { id = item.id, objectiveId = item.objectiveId, description = item.description, category = item.category, critical = item.critical, met = t.HasValue, maxPoints = item.points, points = t.HasValue ? item.points : 0, time = t ?? -1, viaAIIntent = t.HasValue && ViaAI(item.eventId) };
                r.criteria.Add(c);
                if (item.category == "communication") { r.communicationMax += item.points; r.communicationScore += c.points; }
                else { r.observableMax += item.points; r.observableScore += c.points; }
                if (item.critical && !c.met) r.missedCritical.Add(item.description);
            }
            foreach (var o in s.objectives)
            {
                var items = r.criteria.Where(c => c.objectiveId == o.id).ToList();
                r.objectives.Add(new ObjectiveResult { id = o.id, text = o.text, total = items.Count, met = items.Count(c => c.met), complete = items.Count > 0 && items.All(c => c.met) });
            }
            foreach (var te in s.timedExpectations ?? new TimedExpectation[0])
            {
                var t = First(te.eventId);
                r.timed.Add(new TimedResult { eventId = te.eventId, withinSeconds = te.withinSeconds, explanation = te.explanation, actualSeconds = t ?? -1, met = t.HasValue && t.Value <= te.withinSeconds });
            }
            r.escalated = First(ClinicalEvents.EscalationRequested).HasValue;
            return r;
        }
    }

    [Serializable] class SessionHistory { public List<AssessmentResult> sessions = new List<AssessmentResult>(); }

    /// <summary>Local structured storage of session results (JSON in persistentDataPath). No audio, no personal data.</summary>
    public class SessionStore
    {
        readonly string path;
        public SessionStore(string directory = null)
        {
            path = Path.Combine(directory ?? Application.persistentDataPath, "medsim_sessions.json");
        }
        public string FilePath => path;

        public List<AssessmentResult> LoadAll()
        {
            try { if (File.Exists(path)) return JsonUtility.FromJson<SessionHistory>(File.ReadAllText(path))?.sessions ?? new List<AssessmentResult>(); }
            catch (Exception e) { Debug.LogWarning("[MedSim] Could not read session history: " + e.Message); }
            return new List<AssessmentResult>();
        }

        public void Save(AssessmentResult result)
        {
            var h = new SessionHistory { sessions = LoadAll() };
            h.sessions.Add(result);
            if (h.sessions.Count > 50) h.sessions.RemoveRange(0, h.sessions.Count - 50);
            File.WriteAllText(path, JsonUtility.ToJson(h, true));
        }

        public AssessmentResult Previous(string scenarioId)
        {
            var all = LoadAll().Where(x => x.scenarioId == scenarioId).ToList();
            return all.Count >= 2 ? all[all.Count - 2] : null;
        }
    }

    public static class FeedbackComposer
    {
        public static string Compose(ScenarioData s, AssessmentResult r, AssessmentResult previous = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Observable actions:</b> {r.observableScore}/{r.observableMax}   <b>Communication steps:</b> {r.communicationScore}/{r.communicationMax}");
            sb.AppendLine($"<b>Time:</b> {TimeSpan.FromSeconds(r.durationSeconds):mm\\:ss}   <b>Hints requested:</b> {r.hintsRequested}   <b>Escalated:</b> {(r.escalated ? "Yes" : "No")}");
            if (previous != null) sb.AppendLine($"<i>Previous attempt: {previous.observableScore}/{previous.observableMax} observable, {previous.communicationScore}/{previous.communicationMax} communication.</i>");
            sb.AppendLine();
            if (r.missedCritical.Count > 0)
            {
                sb.AppendLine("<color=#FF8A80><b>Safety-critical steps missed:</b></color>");
                foreach (var m in r.missedCritical) sb.AppendLine("  • " + m);
                sb.AppendLine();
            }
            foreach (var t in r.timed)
                sb.AppendLine(t.met ? $"<color=#64FFDA>•</color> Time target met ({t.actualSeconds:0}s). {t.explanation}" : $"<color=#FF8A80>•</color> Time target not met. {t.explanation}");
            sb.AppendLine();
            sb.AppendLine("<b>Teaching points:</b>");
            foreach (var k in s.expectedReasoning.keyPoints) sb.AppendLine("  • " + k);
            sb.AppendLine($"<b>Educational working diagnosis:</b> {s.expectedReasoning.workingDiagnosis}");
            sb.AppendLine();
            sb.AppendLine("<b>References:</b>");
            foreach (var rf in s.references) sb.AppendLine("  • " + rf.citation);
            sb.AppendLine();
            sb.AppendLine("<size=80%><i>" + r.disclaimer + "</i></size>");
            return sb.ToString();
        }
    }
}
