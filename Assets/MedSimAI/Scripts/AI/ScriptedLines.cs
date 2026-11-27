using System.Collections.Generic;
using System.Linq;
using MedSimAI.Clinical;

namespace MedSimAI.AI
{
    /// <summary>
    /// Single source of every approved line the offline mode can speak. Used by the offline provider/orchestrator
    /// and by the voice pre-render tool, so pre-rendered audio keys always match the spoken text.
    /// </summary>
    public static class ScriptedLines
    {
        public const string InstructorReasoningFollowUp = "Thank you. Given those findings, who else needs to be involved, and how urgently?";
        public const string InstructorGeneric = "Good question. Keep focusing on your assessment. Ask me for a hint any time, or tell me when you're ready to summarise.";
        public static string InvestigationArranged(string name) => $"I've arranged the {name}. The result is on the display.";
        public static string InvestigationUnavailable(string name) => $"{name} is not available in this scenario.";

        public static IEnumerable<(Persona persona, string text)> All(ScenarioData s)
        {
            foreach (var f in s.historyFacts) yield return (Persona.Patient, f.answer);
            foreach (var u in s.unknownAnswers) yield return (Persona.Patient, u);
            foreach (GuardCategory g in System.Enum.GetValues(typeof(GuardCategory)))
            {
                if (g == GuardCategory.None) continue;
                yield return (Persona.Patient, InputGuard.SafeResponse(g, Persona.Patient, s));
                yield return (Persona.Instructor, InputGuard.SafeResponse(g, Persona.Instructor, s));
            }
            var l = s.instructorLines;
            foreach (var t in new[] { l.welcome, l.onboardingRole, l.onboardingInstruments, l.tutorial, l.historyDone, l.examDone, l.ecgShown, l.reasoningPrompt, l.escalationAck, l.debriefIntro, l.outOfScope, l.noHintAvailable, InstructorReasoningFollowUp, InstructorGeneric })
                yield return (Persona.Instructor, t);
            foreach (var h in s.hints) yield return (Persona.Instructor, h.text);
            foreach (var i in s.investigations)
                yield return (Persona.Instructor, i.available ? InvestigationArranged(i.name) : InvestigationUnavailable(i.name));
        }

        public static List<(Persona persona, string text)> Distinct(ScenarioData s) =>
            All(s).Where(x => !string.IsNullOrWhiteSpace(x.text)).GroupBy(x => (x.persona, x.text)).Select(g => g.First()).ToList();
    }
}
