using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MedSimAI.Clinical
{
    /// <summary>
    /// Deterministic analysis of what the student says to the supervisor. Separates:
    /// naming a diagnosis, asking a diagnostic question, explicitly requesting escalation, and reasoning concepts.
    /// The rubric only trusts these deterministic results; AI interpretations are tagged and shown as "for instructor review".
    /// </summary>
    public static class StatementAnalysis
    {
        public struct Result
        {
            public bool isQuestion, diagnosisNamed, diagnosticQuestion, escalationRequested, negated;
            public List<string> concepts;
        }

        static readonly string[] QuestionStarts = { "is ", "is it", "could ", "can ", "should ", "would ", "do ", "does ", "what ", "why ", "how ", "are ", "might ", "هل", "شو", "ايش", "إيش", "ممكن", "بتفكري", "معقول" };
        static readonly string[] Negations = { "not ", "don't", "dont", "no need", "won't", "wont", "shouldn't", "never", "مش ", "ما في داعي", "لا داعي" };

        public static string Norm(string s) => HistoryMatcher.Normalize(s).Replace("’", "'").Trim();

        /// <summary>Whole-word match for short ASCII tokens (e.g. "mi", "ii", "avf"), substring match otherwise (and for Arabic).</summary>
        public static bool Contains(string normText, string term)
        {
            var t = Norm(term);
            if (t.Length == 0) return false;
            if (t.Length <= 3 && Regex.IsMatch(t, "^[a-z0-9]+$")) return Regex.IsMatch(normText, $"(^|[^a-z0-9]){Regex.Escape(t)}([^a-z0-9]|$)");
            return normText.Contains(t);
        }
        static bool Any(string normText, IEnumerable<string> terms) => terms != null && terms.Any(k => Contains(normText, k));

        public static List<string> MatchConcepts(ScenarioData s, string utterance)
        {
            var u = Norm(utterance); var outp = new List<string>();
            foreach (var c in s.reasoningConcepts ?? new ReasoningConcept[0])
                if (Any(u, c.anyOf) && (c.alsoAnyOf == null || c.alsoAnyOf.Length == 0 || Any(u, c.alsoAnyOf))) outp.Add(c.id);
            return outp;
        }

        public static bool IsQuestion(string utterance)
        {
            var u = Norm(utterance);
            return u.EndsWith("?") || u.Contains("?") || u.Contains("؟") || QuestionStarts.Any(q => u.StartsWith(q));
        }

        /// <summary>Explicit escalation: an explicit phrase, or an action verb + a target (e.g. "call cardiology"), and not negated.</summary>
        public static bool IsEscalation(ScenarioData s, string utterance)
        {
            var u = Norm(utterance);
            var lex = s.escalation;
            if (lex == null) return HistoryMatcher.MentionsAny(utterance, s.escalationKeywords); // legacy scenarios
            bool explicitHit = Any(u, lex.explicit_);
            bool pair = Any(u, lex.actions) && Any(u, lex.targets);
            if (!explicitHit && !pair) return false;
            // "we don't need to call cardiology" → not an escalation request
            bool negatedNear = Negations.Any(n => { int i = u.IndexOf(n, StringComparison.Ordinal); if (i < 0) return false; var tail = u.Substring(i, Math.Min(40, u.Length - i)); return (lex.actions ?? new string[0]).Concat(lex.explicit_ ?? new string[0]).Any(a => Contains(tail, a)); });
            return !negatedNear;
        }

        public static Result Analyse(ScenarioData s, string utterance)
        {
            var u = Norm(utterance);
            bool q = IsQuestion(utterance);
            bool dx = Any(u, s.diagnosisKeywords);
            return new Result
            {
                isQuestion = q,
                diagnosisNamed = dx && !q,
                diagnosticQuestion = dx && q,
                escalationRequested = IsEscalation(s, utterance),
                concepts = MatchConcepts(s, utterance)
            };
        }
    }
}
