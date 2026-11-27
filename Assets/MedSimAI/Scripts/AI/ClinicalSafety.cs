using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MedSimAI.Clinical;

namespace MedSimAI.AI
{
    /// <summary>Builds the persona-restricted, read-only context. Patient never sees rubric, diagnosis or instructor-only data.</summary>
    public class ClinicalContextProvider : IClinicalContextProvider
    {
        readonly ClinicalSession session;
        public ClinicalContextProvider(ClinicalSession s) { session = s; }

        public PersonaContext BuildContext(Persona persona)
        {
            var s = session.Scenario;
            var ctx = new PersonaContext { persona = persona, stage = session.Stages.Current.ToString(), unknownAnswer = s.unknownAnswers[0] };
            if (persona == Persona.Patient)
            {
                ctx.personaName = s.patient.displayName;
                ctx.roleDescription = $"You are {s.patient.displayName}, a {s.patient.age}-year-old {s.patient.occupation}, a fictional simulated patient lying on an emergency bed with chest pain. Answer only from the facts provided, in first person, briefly and naturally. If a fact is not provided, say you don't know.";
                foreach (var f in s.historyFacts) ctx.allowedFacts.Add(new AllowedFact { id = f.id, text = f.answer });
                ctx.allowedFacts.Add(new AllowedFact { id = "CHIEF", text = s.chiefComplaint });
                ctx.forbiddenTopics.AddRange(new[] { "diagnosis", "ECG interpretation", "grading", "rubric", "treatment", "medication doses", "investigation results" });
            }
            else
            {
                ctx.personaName = "Dr. Sara Hassan (supervising physician)";
                ctx.roleDescription = "You are a supervising emergency physician teaching a medical student in a simulation. Teach by guided questioning. Never reveal the working diagnosis before the debrief. Never invent findings or results. Never give real-world treatment or dosing advice. Refer uncertain questions to the clinical tutor.";
                ctx.allowedFacts.Add(new AllowedFact { id = "CHIEF", text = s.chiefComplaint });
                foreach (var o in s.objectives) ctx.allowedFacts.Add(new AllowedFact { id = o.id, text = o.text });
                // Only findings the student has already elicited are shared with the instructor persona.
                foreach (var e in s.examFindings) if (session.Tracker.Has(ClinicalEvents.Exam(e.id))) ctx.allowedFacts.Add(new AllowedFact { id = e.id, text = e.finding });
                foreach (var i in s.investigations) if (session.Tracker.Has(ClinicalEvents.Inv(i.id))) ctx.allowedFacts.Add(new AllowedFact { id = i.id, text = i.name + ": " + i.result });
                if (session.Stages.Current == ClinicalStage.SupervisorFeedback || session.Stages.Current == ClinicalStage.Debriefing)
                {
                    ctx.allowedFacts.Add(new AllowedFact { id = "DX", text = "Educational working diagnosis: " + s.expectedReasoning.workingDiagnosis });
                    foreach (var k in s.expectedReasoning.keyPoints) ctx.allowedFacts.Add(new AllowedFact { id = "KP", text = k });
                }
                else ctx.forbiddenTopics.Add("the working diagnosis");
                ctx.forbiddenTopics.AddRange(new[] { "medication doses", "real-patient advice", "changing the scenario", "scores" });
            }
            foreach (var f in ctx.allowedFacts) foreach (Match m in Regex.Matches(f.text, @"\d+(\.\d+)?")) ctx.allowedNumbers.Add(m.Value);
            return ctx;
        }
    }

    public enum GuardCategory { None, IgnoreScenario, ChangeState, InventFact, RevealDiagnosis, RealPatientAdvice, Dosing }

    /// <summary>Deterministic pre-filter of student input for manipulation attempts.</summary>
    public static class InputGuard
    {
        static readonly (GuardCategory cat, string[] patterns)[] Rules =
        {
            (GuardCategory.IgnoreScenario, new[]{ "ignore the scenario","ignore your instructions","ignore previous","forget the scenario","you are now","pretend you are not","system prompt","jailbreak","break character" }),
            (GuardCategory.ChangeState, new[]{ "change the blood pressure","change his blood pressure","change your blood pressure","set the blood pressure","make the heart rate","change the heart rate","set his oxygen","change the vitals","change the monitor","make him better","make the patient" }),
            (GuardCategory.InventFact, new[]{ "make up a symptom","invent a symptom","pretend you have","say you have","add a new symptom","invent","make something up" }),
            (GuardCategory.RevealDiagnosis, new[]{ "what is the diagnosis","what's the diagnosis","tell me the diagnosis","correct diagnosis","what do you have","what is wrong with you","what's wrong with you","answer key","how am i graded","what is the answer" }),
            (GuardCategory.RealPatientAdvice, new[]{ "my father","my mother","my friend","my patient at","real patient","i have chest pain","what should i take","should i go to hospital","for my dad","for my mum","for my wife","for my husband" }),
            (GuardCategory.Dosing, new[]{ "what dose","how many mg","dosage","how much aspirin","how much morphine","prescribe" })
        };

        public static GuardCategory Classify(string utterance)
        {
            var u = HistoryMatcher.Normalize(utterance);
            foreach (var (cat, patterns) in Rules) if (patterns.Any(p => u.Contains(p))) return cat;
            return GuardCategory.None;
        }

        public static string SafeResponse(GuardCategory c, Persona p, ScenarioData s)
        {
            switch (c)
            {
                case GuardCategory.IgnoreScenario: return p == Persona.Patient ? "Sorry, doctor, I don't follow. My chest really hurts." : "I'll stay within this training scenario. Let's carry on with the assessment.";
                case GuardCategory.ChangeState: return p == Persona.Patient ? "I don't know how to do that, doctor." : "The patient's observations come from the scenario and can't be changed by conversation. Measure them with the instruments.";
                case GuardCategory.InventFact: return p == Persona.Patient ? "I can only tell you how I actually feel." : "Patient findings are fixed by the scenario. I can't add new ones.";
                case GuardCategory.RevealDiagnosis: return p == Persona.Patient ? "I was hoping you could tell me, doctor." : "I'd like to hear your reasoning first. What do your findings suggest?";
                case GuardCategory.RealPatientAdvice: return "This is a training simulation and cannot give advice about real people. If someone has chest pain now, call your local emergency number.";
                case GuardCategory.Dosing: return p == Persona.Patient ? "I don't know about medicines, sorry." : "Medication dosing is outside the scope of this prototype. Follow your local guidelines and senior advice.";
                default: return s.instructorLines.outOfScope;
            }
        }
    }

    /// <summary>
    /// Enforces grounding on AI output in code. Rejects: unknown fact ids, numbers not present in the allowed context,
    /// diagnosis disclosure where forbidden, dosing, state-change claims, invented references.
    /// </summary>
    public class ResponseValidator : IResponseValidator
    {
        static readonly string[] DosingPatterns = { @"\b\d+\s?(mg|mcg|µg|ml|units?|iu)\b", @"\bdose\b", @"\bprescrib" };
        static readonly string[] StateChangePatterns = { "i have changed", "i've changed", "i set the", "blood pressure is now", "heart rate is now", "i've updated", "i have updated" };
        static readonly string[] DiagnosisTerms = { "stemi", "myocardial infarction", "heart attack", "st elevation", "st-elevation", "acute coronary syndrome", "nstemi", "angina", "pericarditis", "aortic dissection", "pulmonary embol" };
        static readonly string[] ReferencePatterns = { "et al", "doi:", "according to the guideline", "journal of", "http" };

        public ValidationResult Validate(AIResponse r, PersonaContext ctx)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.speech)) return Fail("empty");
            var text = r.speech.ToLowerInvariant();
            if (r.speech.Length > 600) return Fail("too long");
            var ids = new HashSet<string>(ctx.allowedFacts.Select(f => f.id));
            foreach (var id in r.usedFactIds ?? new string[0]) if (!ids.Contains(id)) return Fail("unknown fact id " + id);
            foreach (var p in DosingPatterns) if (Regex.IsMatch(text, p)) return Fail("dosing content");
            foreach (var p in StateChangePatterns) if (text.Contains(p)) return Fail("state change claim");
            foreach (var p in ReferencePatterns) if (text.Contains(p)) return Fail("unsupported reference");
            bool dxAllowed = ctx.allowedFacts.Any(f => f.id == "DX");
            if (!dxAllowed) foreach (var d in DiagnosisTerms) if (text.Contains(d)) return Fail("diagnosis disclosure");
            var allowedNums = new HashSet<string>(ctx.allowedNumbers);
            foreach (Match m in Regex.Matches(text, @"\d+(\.\d+)?"))
                if (!allowedNums.Contains(m.Value)) return Fail("unsupported number " + m.Value);
            // Patient answers must be grounded in at least one fact unless it is an explicit "don't know".
            if (ctx.persona == Persona.Patient && (r.usedFactIds == null || r.usedFactIds.Length == 0) && !LooksLikeUnknown(text) && !r.outOfScope)
                return Fail("ungrounded patient statement");
            return new ValidationResult { valid = true };
        }

        static bool LooksLikeUnknown(string t) => t.Contains("don't know") || t.Contains("not sure") || t.Contains("can't remember") || t.Contains("cannot remember");
        static ValidationResult Fail(string reason) => new ValidationResult { valid = false, reason = reason };
    }
}
