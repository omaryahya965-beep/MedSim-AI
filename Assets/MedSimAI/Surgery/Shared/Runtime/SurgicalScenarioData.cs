using System;
using System.Collections.Generic;
using System.Linq;

namespace MedSimAI.Surgery.Shared
{
    // Authored surgical scenario schema (JSON). Independent from the ER ScenarioData: no chest-pain facts, personas or
    // rubric can leak in. JsonUtility-compatible (fields, arrays, no dictionaries). Loading is injected so this stays
    // engine-free: Unity uses JsonUtility.FromJson, off-Unity tests use System.Text.Json.
    // File: Assets/MedSimAI/Surgery/Scenarios/Resources/SurgeryScenarios/<scenarioId>.json  (owner: Person 4,
    // objective mechanics fields agreed with Person 3).

    [Serializable] public class LocalizedText
    {
        public string en = "", ar = "";
        public string Get(string lang) => lang == "ar" && !string.IsNullOrEmpty(ar) ? ar : en;
        public bool IsComplete => !string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(ar);
    }

    [Serializable] public class SurgicalReviewInfo
    {
        public string status = "DRAFT";          // DRAFT | IN_REVIEW | APPROVED
        public string reviewer = "";             // qualified orthopaedic surgeon, filled in by the project owner only
        public string reviewedOn = "";
        public string notes = "";
    }

    [Serializable] public class SurgicalObjective
    {
        public string id = "";                   // obj.*
        public int order;
        public LocalizedText title = new LocalizedText();
        public LocalizedText explanation = new LocalizedText();   // approved educational explanation (what and why, at simulation level)
        public string[] prerequisites = new string[0];            // objective ids that must be completed first
        public string[] requiredTools = new string[0];            // tools that can complete it
        public string[] targets = new string[0];                  // anatomy targets involved
        public string completionEvent = "";                       // usually objective.completed (from mechanics)
        public string completionState = "";                       // expected anatomy state after completion (informative)
        public string implantId = "";                             // for placement objectives
        public float toleranceMm = -1, toleranceDeg = -1;         // placement tolerances (simulation-defined, DRAFT)
        public bool safetyCritical;                               // rejected interactions here are reported as safety observations
        public LocalizedText[] hints = new LocalizedText[0];      // ordered, least to most specific
    }

    [Serializable] public class SurgicalInstrumentInfo { public string id = ""; public LocalizedText name = new LocalizedText(); public LocalizedText purpose = new LocalizedText(); public string[] aliases = new string[0]; }
    [Serializable] public class SurgicalTargetInfo { public string id = ""; public LocalizedText name = new LocalizedText(); public string[] states = new string[0]; }
    [Serializable] public class SurgicalImplantInfo { public string id = ""; public LocalizedText name = new LocalizedText(); public string targetId = ""; }

    [Serializable] public class SupervisorTrigger
    {
        public string type = "";        // rejected | repeated_rejection | idle | help_requested | objective_started | session_started | objective_completed | session_completed
        public string reason = "";      // for rejected/repeated_rejection: RejectReasons.* ("" = any)
        public string objectiveId = ""; // "" = any objective
        public int count = 1;           // repeated_rejection threshold
        public float seconds;           // idle threshold
    }

    [Serializable] public class SupervisorRule
    {
        public string id = "";
        public string kind = "hint";    // hint | intervention | safety | welcome | praise
        public SupervisorTrigger trigger = new SupervisorTrigger();
        public int priority = 1;        // higher wins when several fire on the same tick
        public float graceSeconds;      // wait this long after the trigger (student may self-correct)
        public float cooldownSeconds = 15;
        public int maxTimes = 1;
        public LocalizedText line = new LocalizedText();   // approved line; AI may only rephrase it within grounding rules
        public bool givesHint;          // counts as a hint in the assessment
    }

    [Serializable] public class SupervisorPolicySettings
    {
        public float globalCooldownSeconds = 12;
        public bool silentOnCorrectActions = true;
        public int maxHintsPerObjective = 3;
    }

    [Serializable] public class AssessmentCriterion
    {
        public string id = "";
        public LocalizedText label = new LocalizedText();
        public string kind = "";        // objective_completed | objective_clean | placement_within_tolerance | prerequisites_respected | hints_at_most | interventions_at_most | session_completed | duration_at_most
        public string objectiveId = "";
        public string implantId = "";
        public float threshold;         // count or seconds, depending on kind
        public int weight = 1;
    }

    [Serializable] public class SurgicalPersona
    {
        public string id = "";          // SurgeryCharacterIds.*
        public LocalizedText displayName = new LocalizedText();
        public string voice = "";       // proxy voice id
        public string summary = "", speechStyle = "", boundaries = "";
    }

    [Serializable] public class AssistantPolicy
    {
        public string[] handoverTools = new string[0];     // tools the assistant may hand over
        public string[] deniedActions = new string[0];     // catalogue actions never allowed in this scenario
        public bool handoverOnlyForCurrentObjective = true;// otherwise the assistant explains it is not needed yet
    }

    [Serializable] public class SafetyObservationDef { public string reason = ""; public LocalizedText text = new LocalizedText(); }

    [Serializable] public class SurgicalScenarioData
    {
        public string scenarioId = "";
        public string version = "";
        public string contractVersion = "";
        public string kind = "surgical";
        public LocalizedText title = new LocalizedText();
        public LocalizedText summary = new LocalizedText();
        public string operativeSide = "right";
        public SurgicalReviewInfo review = new SurgicalReviewInfo();
        public LocalizedText[] limitations = new LocalizedText[0];
        public string[] references = new string[0];
        public SurgicalPersona[] personas = new SurgicalPersona[0];
        public LocalizedText welcome = new LocalizedText();
        public SurgicalObjective[] objectives = new SurgicalObjective[0];
        public SurgicalInstrumentInfo[] instruments = new SurgicalInstrumentInfo[0];
        public SurgicalTargetInfo[] targets = new SurgicalTargetInfo[0];
        public SurgicalImplantInfo[] implants = new SurgicalImplantInfo[0];
        public SupervisorPolicySettings supervisorPolicy = new SupervisorPolicySettings();
        public SupervisorRule[] supervisorRules = new SupervisorRule[0];
        public AssessmentCriterion[] assessment = new AssessmentCriterion[0];
        public AssistantPolicy assistantPolicy = new AssistantPolicy();
        public SafetyObservationDef[] safetyObservations = new SafetyObservationDef[0];

        public bool IsClinicallyValidated => review != null && review.status == "APPROVED" && !string.IsNullOrWhiteSpace(review.reviewer);
        public string ValidationBanner(string lang) => IsClinicallyValidated ? "" : (lang == "ar" ? SurgeryContract.VALIDATION_BANNER_AR : SurgeryContract.VALIDATION_BANNER);
        public SurgicalObjective Objective(string id) => objectives.FirstOrDefault(o => o.id == id);
        public SurgicalInstrumentInfo Instrument(string id) => instruments.FirstOrDefault(i => i.id == id);
        public SurgicalPersona Persona(string id) => personas.FirstOrDefault(p => p.id == id);
        public IEnumerable<SurgicalObjective> Ordered => objectives.OrderBy(o => o.order);
    }

    /// <summary>Structural validation (ids, references, localisation, review gate). Returns human-readable errors.</summary>
    public static class SurgicalScenarioValidator
    {
        public static List<string> Validate(SurgicalScenarioData s)
        {
            var e = new List<string>();
            if (s == null) { e.Add("scenario is null"); return e; }
            if (string.IsNullOrEmpty(s.scenarioId) || string.IsNullOrEmpty(s.version)) e.Add("missing scenarioId/version");
            if (s.kind != "surgical") e.Add("kind must be 'surgical'");
            if (s.contractVersion.Split('.')[0] != SurgeryContract.CONTRACT_VERSION.Split('.')[0]) e.Add($"contract major version mismatch: {s.contractVersion} vs {SurgeryContract.CONTRACT_VERSION}");
            if (!s.IsClinicallyValidated && s.review?.status == "APPROVED") e.Add("APPROVED status requires a named reviewer");
            if (!(s.limitations?.Length > 0)) e.Add("limitations are required");
            Dup(e, "objective", s.objectives.Select(o => o.id)); Dup(e, "rule", s.supervisorRules.Select(r => r.id)); Dup(e, "criterion", s.assessment.Select(a => a.id));
            var objIds = new HashSet<string>(s.objectives.Select(o => o.id));
            var toolIds = new HashSet<string>(s.instruments.Select(i => i.id));
            var targetIds = new HashSet<string>(s.targets.Select(t => t.id));
            foreach (var t in toolIds) if (!InstrumentIds.All.Contains(t)) e.Add("instrument not in contract: " + t);
            foreach (var t in targetIds) if (!AnatomyTargetIds.All.Contains(t)) e.Add("target not in contract: " + t);
            foreach (var i in s.implants) if (!ImplantIds.All.Contains(i.id)) e.Add("implant not in contract: " + i.id);
            foreach (var o in s.objectives)
            {
                if (!o.title.IsComplete || !o.explanation.IsComplete) e.Add($"objective {o.id}: en+ar title/explanation required");
                foreach (var p in o.prerequisites) if (!objIds.Contains(p)) e.Add($"objective {o.id}: unknown prerequisite {p}");
                foreach (var t in o.requiredTools) if (!toolIds.Contains(t)) e.Add($"objective {o.id}: unknown tool {t}");
                foreach (var t in o.targets) if (!targetIds.Contains(t)) e.Add($"objective {o.id}: unknown target {t}");
                if (!string.IsNullOrEmpty(o.completionEvent) && !SurgeryEventIds.IsKnown(o.completionEvent)) e.Add($"objective {o.id}: unknown completion event {o.completionEvent}");
                foreach (var h in o.hints) if (!h.IsComplete) e.Add($"objective {o.id}: hint missing en/ar");
            }
            foreach (var r in s.supervisorRules)
            {
                if (!r.line.IsComplete) e.Add($"rule {r.id}: en+ar line required");
                if (!string.IsNullOrEmpty(r.trigger.objectiveId) && !objIds.Contains(r.trigger.objectiveId)) e.Add($"rule {r.id}: unknown objective {r.trigger.objectiveId}");
            }
            foreach (var a in s.assessment)
            {
                if (!string.IsNullOrEmpty(a.objectiveId) && !objIds.Contains(a.objectiveId)) e.Add($"criterion {a.id}: unknown objective {a.objectiveId}");
                if (!a.label.IsComplete) e.Add($"criterion {a.id}: en+ar label required");
            }
            foreach (var t in s.assistantPolicy.handoverTools) if (!toolIds.Contains(t)) e.Add("assistant handover tool unknown: " + t);
            if (s.Persona(SurgeryCharacterIds.Surgeon) == null || s.Persona(SurgeryCharacterIds.Assistant) == null) e.Add("surgeon and assistant personas required");
            return e;
        }
        static void Dup(List<string> e, string what, IEnumerable<string> ids) { foreach (var g in ids.GroupBy(x => x).Where(g => g.Count() > 1 || string.IsNullOrEmpty(g.Key))) e.Add($"duplicate or empty {what} id: '{g.Key}'"); }
    }
}
