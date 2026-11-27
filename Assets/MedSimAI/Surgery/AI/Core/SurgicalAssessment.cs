using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MedSimAI.Surgery.Shared;

namespace MedSimAI.Surgery.AI
{
    public enum CriterionStatus { Met, NotMet, NotAssessed }

    [Serializable] public class SurgicalCriterionResult
    {
        public string id = "", kind = "", labelEn = "", labelAr = "", objectiveId = "", note = "";
        public CriterionStatus status; public int weight; public long[] evidenceSeq = new long[0];
        public bool Met => status == CriterionStatus.Met;
        public string Label(string lang) => lang == "ar" && !string.IsNullOrEmpty(labelAr) ? labelAr : labelEn;
    }

    [Serializable] public class ObjectiveOutcome { public string id = "", titleEn = "", titleAr = ""; public bool completed; public float completedAt = -1; public int rejections; }

    [Serializable] public class SurgicalAssessmentResult
    {
        public string scenarioId = "", scenarioVersion = "", sessionId = "", validationBanner = "", reviewStatus = "";
        public bool sessionCompleted;
        public float durationSeconds;
        public int score, maxScore;
        public List<SurgicalCriterionResult> criteria = new List<SurgicalCriterionResult>();
        public List<ObjectiveOutcome> objectives = new List<ObjectiveOutcome>();
        public int validInteractions, invalidInteractions, hintsGiven, helpRequests, interventions, placementsAttempted, placementsCompleted;
        public Dictionary<string, int> rejectionsByReason = new Dictionary<string, int>();
        public List<string> safetyObservationsEn = new List<string>(), safetyObservationsAr = new List<string>();
        public List<string> strengthsEn = new List<string>(), strengthsAr = new List<string>(), improvementsEn = new List<string>(), improvementsAr = new List<string>();
        /// <summary>Gameplay-looking events NOT from mechanics (AI/UI/mock) that were ignored for credit.</summary>
        public int ignoredUnverifiedEvents;
        public IEnumerable<ObjectiveOutcome> Completed => objectives.Where(o => o.completed);
        public IEnumerable<ObjectiveOutcome> Missed => objectives.Where(o => !o.completed);
        public float Percent => maxScore == 0 ? 0 : 100f * score / maxScore;
    }

    /// <summary>
    /// Deterministic surgical assessment. Only gameplay events published by mechanics earn credit; hints and
    /// interventions are counted from the supervisor's own events. AI interpretation, verbal claims and mock events never
    /// score. Not a competency certification.
    /// </summary>
    public static class SurgicalAssessmentEngine
    {
        public static SurgicalAssessmentResult Evaluate(SurgicalScenarioData s, IReadOnlyList<SurgeryEvent> log, bool acceptMockAsMechanics = false)
        {
            var r = new SurgicalAssessmentResult { scenarioId = s.scenarioId, scenarioVersion = s.version, validationBanner = s.ValidationBanner("en"), reviewStatus = s.review?.status ?? "DRAFT" };
            log ??= new List<SurgeryEvent>();
            r.sessionId = log.FirstOrDefault()?.sessionId ?? "";
            bool Verified(SurgeryEvent e) => e.source == SurgeryEventSources.Mechanics || (acceptMockAsMechanics && e.source == SurgeryEventSources.Mock);
            var g = log.Where(e => SurgeryEventIds.IsGameplay(e.id) && Verified(e)).ToList();
            r.ignoredUnverifiedEvents = log.Count(e => SurgeryEventIds.IsGameplay(e.id) && !Verified(e));

            var completedAt = new Dictionary<string, SurgeryEvent>();
            foreach (var e in g.Where(e => e.id == SurgeryEventIds.ObjectiveCompleted)) if (!completedAt.ContainsKey(e.objectiveId)) completedAt[e.objectiveId] = e;
            var rejected = g.Where(e => e.id == SurgeryEventIds.InteractionRejected).ToList();

            foreach (var o in s.Ordered)
                r.objectives.Add(new ObjectiveOutcome { id = o.id, titleEn = o.title.en, titleAr = o.title.ar, completed = completedAt.ContainsKey(o.id), completedAt = completedAt.TryGetValue(o.id, out var ce) ? ce.t : -1, rejections = rejected.Count(x => x.objectiveId == o.id) });

            r.validInteractions = g.Count(e => e.id == SurgeryEventIds.AnatomyInteracted || e.id == SurgeryEventIds.ImplantPlacementCompleted);
            r.invalidInteractions = rejected.Count;
            foreach (var grp in rejected.GroupBy(x => string.IsNullOrEmpty(x.reason) ? "invalid" : x.reason)) r.rejectionsByReason[grp.Key] = grp.Count();
            r.placementsAttempted = g.Count(e => e.id == SurgeryEventIds.ImplantPlacementAttempted);
            r.placementsCompleted = g.Count(e => e.id == SurgeryEventIds.ImplantPlacementCompleted);
            r.hintsGiven = log.Count(e => e.id == SurgeryEventIds.HintGiven && e.source == SurgeryEventSources.AI);
            r.interventions = log.Count(e => e.id == SurgeryEventIds.SupervisorIntervened && e.source == SurgeryEventSources.AI);
            r.helpRequests = log.Count(e => e.id == SurgeryEventIds.HelpRequested);

            bool allDone = s.objectives.All(o => completedAt.ContainsKey(o.id));
            var completedEvt = log.LastOrDefault(e => e.id == SurgeryEventIds.SessionCompleted && (e.source == SurgeryEventSources.Integration || acceptMockAsMechanics));
            r.sessionCompleted = completedEvt != null && allDone;
            var start = log.FirstOrDefault(e => e.id == SurgeryEventIds.SessionStarted);
            var end = completedEvt ?? log.LastOrDefault();
            r.durationSeconds = end == null ? 0 : Math.Max(0, end.t - (start?.t ?? 0));

            foreach (var c in s.assessment) r.criteria.Add(Judge(c, s, r, g, completedAt));
            r.maxScore = r.criteria.Where(c => c.status != CriterionStatus.NotAssessed).Sum(c => c.weight);
            r.score = r.criteria.Where(c => c.Met).Sum(c => c.weight);

            // Safety observations: scenario-defined reasons, plus any rejection inside a safety-critical objective.
            foreach (var reason in r.rejectionsByReason.Keys)
            {
                var def = s.safetyObservations.FirstOrDefault(d => d.reason == reason);
                if (def != null) { r.safetyObservationsEn.Add($"{def.text.en} (×{r.rejectionsByReason[reason]})"); r.safetyObservationsAr.Add($"{def.text.ar} (×{r.rejectionsByReason[reason]})"); }
            }
            foreach (var o in s.objectives.Where(o => o.safetyCritical))
            {
                int n = rejected.Count(x => x.objectiveId == o.id && s.safetyObservations.All(d => d.reason != x.reason));
                if (n > 0) { r.safetyObservationsEn.Add($"{n} invalid attempt(s) during a safety-critical step: {o.title.en}."); r.safetyObservationsAr.Add($"{n} محاولة غير صحيحة أثناء خطوة حساسة للسلامة: {o.title.ar}."); }
            }
            Feedback(r, s);
            return r;
        }

        static SurgicalCriterionResult Judge(AssessmentCriterion c, SurgicalScenarioData s, SurgicalAssessmentResult r, List<SurgeryEvent> g, Dictionary<string, SurgeryEvent> completedAt)
        {
            var x = new SurgicalCriterionResult { id = c.id, kind = c.kind, labelEn = c.label.en, labelAr = c.label.ar, objectiveId = c.objectiveId, weight = c.weight };
            void Set(bool met, string note = "", params SurgeryEvent[] ev) { x.status = met ? CriterionStatus.Met : CriterionStatus.NotMet; x.note = note; x.evidenceSeq = ev.Where(e => e != null).Select(e => e.seq).ToArray(); }
            switch (c.kind)
            {
                case "objective_completed":
                    completedAt.TryGetValue(c.objectiveId, out var done); Set(done != null, done == null ? "not completed" : "", done); break;
                case "objective_clean":
                {
                    completedAt.TryGetValue(c.objectiveId, out var d);
                    var rej = g.Where(e => e.id == SurgeryEventIds.InteractionRejected && e.objectiveId == c.objectiveId).ToArray();
                    Set(d != null && rej.Length == 0, d == null ? "not completed" : rej.Length > 0 ? rej.Length + " invalid attempt(s)" : "", rej.Length > 0 ? rej : new[] { d });
                    break;
                }
                case "placement_within_tolerance":
                {
                    var o = s.Objective(c.objectiveId);
                    var p = g.LastOrDefault(e => e.id == SurgeryEventIds.ImplantPlacementCompleted && e.implantId == c.implantId);
                    if (p == null) { Set(false, "not placed"); break; }
                    if (p.positionErrorMm < 0 && p.angleErrorDeg < 0) { x.status = CriterionStatus.NotAssessed; x.note = "accuracy not measured by mechanics"; x.evidenceSeq = new[] { p.seq }; break; }
                    bool posOk = p.positionErrorMm < 0 || o == null || o.toleranceMm < 0 || p.positionErrorMm <= o.toleranceMm;
                    bool angOk = p.angleErrorDeg < 0 || o == null || o.toleranceDeg < 0 || p.angleErrorDeg <= o.toleranceDeg;
                    Set(posOk && angOk, string.Format(CultureInfo.InvariantCulture, "{0:0.#} mm / {1:0.#}°", p.positionErrorMm, p.angleErrorDeg), p);
                    break;
                }
                case "prerequisites_respected":
                {
                    var bad = g.Where(e => e.id == SurgeryEventIds.InteractionRejected && e.reason == RejectReasons.PrerequisiteMissing).ToList();
                    // also verify mechanics never completed an objective before its prerequisites
                    foreach (var kv in completedAt)
                    {
                        var o = s.Objective(kv.Key); if (o == null) continue;
                        foreach (var pre in o.prerequisites) if (!completedAt.TryGetValue(pre, out var pe) || pe.seq > kv.Value.seq) bad.Add(kv.Value);
                    }
                    if (!r.Completed.Any()) { Set(false, "no steps completed"); break; }
                    Set(bad.Count == 0, bad.Count > 0 ? bad.Count + " out-of-order attempt(s)" : "", bad.ToArray());
                    break;
                }
                case "hints_at_most":
                    if (!r.sessionCompleted) Set(false, "session not completed"); else Set(r.hintsGiven <= c.threshold, r.hintsGiven + " hint(s)"); break;
                case "interventions_at_most":
                    if (!r.sessionCompleted) Set(false, "session not completed"); else Set(r.interventions <= c.threshold, r.interventions + " intervention(s)"); break;
                case "session_completed":
                    Set(r.sessionCompleted, r.sessionCompleted ? "" : "not all steps completed"); break;
                case "duration_at_most":
                    if (!r.sessionCompleted) Set(false, "session not completed"); else Set(r.durationSeconds <= c.threshold, Mmss(r.durationSeconds)); break;
                default:
                    x.status = CriterionStatus.NotAssessed; x.note = "unknown criterion kind"; break;
            }
            return x;
        }

        static void Feedback(SurgicalAssessmentResult r, SurgicalScenarioData s)
        {
            void Str(string en, string ar) { r.strengthsEn.Add(en); r.strengthsAr.Add(ar); }
            void Imp(string en, string ar) { r.improvementsEn.Add(en); r.improvementsAr.Add(ar); }
            var siteObj = s.objectives.FirstOrDefault(o => o.safetyCritical && o.order == s.objectives.Min(x => x.order));
            if (siteObj != null && r.objectives.Any(o => o.id == siteObj.id && o.completed && o.rejections == 0)) Str("Confirmed the operative site cleanly before starting.", "تأكدت من مكان العملية بشكل صحيح قبل البدء.");
            var clean = r.objectives.Where(o => o.completed && o.rejections == 0).ToList();
            if (clean.Count >= 3) Str($"Completed {clean.Count} steps without any invalid attempt.", $"أكملت {clean.Count} خطوات بدون ولا محاولة غلط.");
            if (r.sessionCompleted) Str("Completed the whole simulated procedure.", "أكملت العملية الافتراضية كاملة.");
            if (r.sessionCompleted && r.hintsGiven == 0) Str("Worked independently without hints.", "اشتغلت باستقلالية بدون تلميحات.");
            if (r.criteria.Any(c => c.kind == "placement_within_tolerance" && c.Met)) Str("Placed implant components within the simulation tolerance.", "ركّبت مكونات المفصل ضمن حدود دقة المحاكاة.");

            if (r.rejectionsByReason.TryGetValue(RejectReasons.WrongSide, out var ws)) Imp($"Always check the operative side before acting ({ws} wrong-side attempt(s)).", $"دايماً تأكد من جهة العملية قبل أي إجراء ({ws} محاولة على الجهة الخطأ).");
            if (r.rejectionsByReason.TryGetValue(RejectReasons.PrerequisiteMissing, out var pm)) Imp($"Follow the step order; {pm} attempt(s) came before their prerequisite.", $"التزم بترتيب الخطوات؛ {pm} محاولة كانت قبل وقتها.");
            if (r.rejectionsByReason.TryGetValue(RejectReasons.WrongTool, out var wt) && wt >= 2) Imp("Review which instrument each step needs.", "راجع شو الأداة اللي بتحتاجها كل خطوة.");
            if (r.rejectionsByReason.TryGetValue(RejectReasons.OutOfTolerance, out var ot)) Imp("Take more care with alignment when placing components.", "انتبه أكثر للاصطفاف وقت تركيب المكونات.");
            foreach (var o in r.Missed.Take(3)) Imp($"Not completed: {o.titleEn}.", $"ما اكتملت: {o.titleAr}.");
            if (r.hintsGiven > 4) Imp("Try to rely less on hints next time.", "حاول تعتمد أقل على التلميحات المرة الجاية.");
            if (r.strengthsEn.Count == 0) Str("You started the simulation and engaged with the steps.", "بلّشت المحاكاة وتفاعلت مع الخطوات.");
        }

        public static string Mmss(float sec) { int s = (int)Math.Round(sec); return $"{s / 60}:{s % 60:00}"; }
    }

    /// <summary>Written debrief (en/ar) and grounded facts for the spoken debrief. Everything comes from the result.</summary>
    public static class SurgicalDebriefComposer
    {
        public static string Written(SurgicalAssessmentResult r, string lang)
        {
            bool ar = lang == "ar"; var b = new StringBuilder();
            string L(string en, string a) => ar ? a : en;
            b.AppendLine(L("Surgical training debrief", "تقرير تقييم التدريب الجراحي"));
            b.AppendLine(ar ? SurgeryContract.VALIDATION_BANNER_AR : SurgeryContract.VALIDATION_BANNER);
            b.AppendLine(L($"Score: {r.score}/{r.maxScore} ({r.Percent:0}%) · Time: {SurgicalAssessmentEngine.Mmss(r.durationSeconds)} · Hints: {r.hintsGiven} · Interventions: {r.interventions}",
                           $"النتيجة: {r.score}/{r.maxScore} ({r.Percent:0}%) · الوقت: {SurgicalAssessmentEngine.Mmss(r.durationSeconds)} · التلميحات: {r.hintsGiven} · التدخلات: {r.interventions}"));
            b.AppendLine(L($"Valid interactions: {r.validInteractions} · Invalid interactions: {r.invalidInteractions}", $"تفاعلات صحيحة: {r.validInteractions} · تفاعلات غير صحيحة: {r.invalidInteractions}"));
            b.AppendLine().AppendLine(L("Completed objectives", "الأهداف المكتملة"));
            foreach (var o in r.Completed) b.AppendLine("  ✓ " + (ar ? o.titleAr : o.titleEn));
            if (!r.Completed.Any()) b.AppendLine("  —");
            b.AppendLine(L("Missed objectives", "الأهداف غير المكتملة"));
            foreach (var o in r.Missed) b.AppendLine("  ✗ " + (ar ? o.titleAr : o.titleEn));
            if (!r.Missed.Any()) b.AppendLine("  —");
            b.AppendLine().AppendLine(L("Criteria", "المعايير"));
            foreach (var c in r.criteria) b.AppendLine($"  {(c.status == CriterionStatus.Met ? "✓" : c.status == CriterionStatus.NotMet ? "✗" : "–")} {c.Label(lang)}{(string.IsNullOrEmpty(c.note) ? "" : " (" + c.note + ")")}");
            var safety = ar ? r.safetyObservationsAr : r.safetyObservationsEn;
            b.AppendLine().AppendLine(L("Safety observations (scenario-defined)", "ملاحظات السلامة (حسب السيناريو)"));
            foreach (var x in safety) b.AppendLine("  • " + x);
            if (safety.Count == 0) b.AppendLine("  " + L("None recorded.", "ما في ملاحظات مسجلة."));
            b.AppendLine().AppendLine(L("Strengths", "نقاط القوة"));
            foreach (var x in ar ? r.strengthsAr : r.strengthsEn) b.AppendLine("  • " + x);
            b.AppendLine(L("Areas for improvement", "نقاط للتحسين"));
            foreach (var x in ar ? r.improvementsAr : r.improvementsEn) b.AppendLine("  • " + x);
            if (r.ignoredUnverifiedEvents > 0) b.AppendLine().AppendLine(L($"{r.ignoredUnverifiedEvents} unverified event(s) were not scored.", $"{r.ignoredUnverifiedEvents} حدث غير موثّق ما انحسب بالتقييم."));
            b.AppendLine().AppendLine(L("Scores reflect observable simulation actions only. This is not a surgical competency certification and does not replace clinical supervision.",
                                       "النتيجة بتعكس الإجراءات المرصودة بالمحاكاة فقط. هاد مش شهادة كفاءة جراحية وما بيغني عن الإشراف السريري."));
            return b.ToString();
        }

        /// <summary>Deterministic spoken debrief (offline fallback, and the approved base text the AI may rephrase).</summary>
        public static string SpokenBase(SurgicalAssessmentResult r, string lang)
        {
            bool ar = lang == "ar";
            int done = r.Completed.Count(), total = r.objectives.Count;
            var s = ar ? r.strengthsAr.FirstOrDefault() : r.strengthsEn.FirstOrDefault();
            var i = ar ? r.improvementsAr.FirstOrDefault() : r.improvementsEn.FirstOrDefault();
            return ar
                ? $"خلصنا. أكملت {done} من {total} خطوات، وأخذت {r.hintsGiven} تلميحات. {s} {(string.IsNullOrEmpty(i) ? "" : "للمرة الجاية: " + i)} تذكّر إنه هاي محاكاة تعليمية غير معتمدة سريرياً."
                : $"We're done. You completed {done} of {total} steps and used {r.hintsGiven} hints. {s} {(string.IsNullOrEmpty(i) ? "" : "For next time: " + i)} Remember, this is an educational simulation that is not clinically validated.";
        }

        /// <summary>Compact, id-level facts sent to the proxy for the spoken debrief (no free text from the client).</summary>
        public static DebriefFacts Facts(SurgicalAssessmentResult r) => new DebriefFacts
        {
            completed = r.Completed.Select(o => o.id).ToArray(), missed = r.Missed.Select(o => o.id).ToArray(),
            criteriaMet = r.criteria.Where(c => c.Met).Select(c => c.id).ToArray(), criteriaMissed = r.criteria.Where(c => c.status == CriterionStatus.NotMet).Select(c => c.id).ToArray(),
            rejectionReasons = r.rejectionsByReason.Keys.ToArray(), rejectionCounts = r.rejectionsByReason.Values.ToArray(),
            hints = r.hintsGiven, interventions = r.interventions, durationSeconds = r.durationSeconds, sessionCompleted = r.sessionCompleted, score = r.score, maxScore = r.maxScore
        };
    }

    [Serializable] public class DebriefFacts
    {
        public string[] completed, missed, criteriaMet, criteriaMissed, rejectionReasons; public int[] rejectionCounts;
        public int hints, interventions, score, maxScore; public float durationSeconds; public bool sessionCompleted;
    }
}
