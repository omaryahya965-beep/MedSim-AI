using System.Text.RegularExpressions;

namespace MedSimAI.AI
{
    /// <summary>Client-side re-validation of AI conversational wording (defence in depth; mirrors the proxy rules).</summary>
    public static class GroundedText
    {
        static readonly Regex Digits = new Regex("[0-9٠-٩]");
        static readonly Regex Clinical = new Regex(@"(pain|ache|chest|arm|jaw|breath|sweat|sick|nause|vomit|dizzy|allerg|tablet|pill|medic|smok|pressure|heart|sugar|cholesterol|family|father|mother|stemi|infarct|attack|dose|mg\b|ألم|صدر|ذراع|فك|تنفس|تعرق|غثيان|حساسية|دواء|تدخين|ضغط|قلب|سكر|جرعة)", RegexOptions.IgnoreCase);

        public static bool ValidLeadIn(string s) =>
            !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= 60 && !Digits.IsMatch(s) && !Clinical.IsMatch(s);
    }
}
