using System.Linq;
using System.Text.RegularExpressions;

namespace MedSimAI.AI
{
    /// <summary>
    /// Application-level conversation language shared by BOTH characters.
    ///  • Auto (default): each reply follows the language the student just used; the conversation language (director
    ///    lines, interventions, discussion, debrief, subtitles, tablet text) follows the student's latest language.
    ///  • Arabic / English: chosen explicitly (voice command such as «احكي عربي» / "speak English", or the pause menu);
    ///    both characters stay in that language until the student switches again.
    /// </summary>
    public enum LanguageMode { Auto = 0, Arabic = 1, English = 2 }

    /// <summary>Language detection and explicit language commands (mirrors Backend/medsim-proxy/lib/language.mjs).</summary>
    public static class LanguageCommands
    {
        static readonly Regex ArWord = new Regex(@"[ء-يٱ-ۓۺ-ۿ]+");
        static readonly Regex LatinWord = new Regex(@"[A-Za-z]{2,}");
        static readonly Regex HasArabicLang = new Regex("(عربي|العربي|بالعربي|العربيه|بالعربيه|عربيه)");
        static readonly Regex HasEnglishLang = new Regex("(انجليزي|انكليزي|انقليزي|الانجليزي|بالانجليزي|بالانكليزي|الانجليزيه|بالانجليزيه|انجلش|انغلش|english)");
        static readonly Regex Verb = new Regex("(احكي|احكو|احكوا|حكي|نحكي|تحكي|تحكيلي|تحكو|حول|حولي|نحول|غير|غيري|نغير|كمل|كملي|نكمل|تكمل|بدي|بدنا|خلينا|خليني|ممكن|لو سمحت|رجع|ارجع|نرجع|رد|ردي|جاوب|جاوبني|اتكلم|تكلم|نتكلم|تتكلم|تحدث|نتحدث|يا ريت|من فضلك|صار|خلص)");
        static readonly Regex EnSwitch = new Regex(@"\b(speak|talk|answer|reply|respond|continue|switch|change|use|let'?s (?:speak|talk|go|continue|switch)|can you|could you|please)\b[^.?!]{0,40}\b(arabic|english)\b|\bin (arabic|english),? please\b|\b(switch|change) (?:the )?language to (arabic|english)\b|^(arabic|english),? please$|^(in )?(arabic|english)$");

        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormKC);
            s = Regex.Replace(s, "[ً-ْـ]", "");
            s = Regex.Replace(s, "[إأآ]", "ا").Replace('ى', 'ي').Replace('ة', 'ه');
            s = Regex.Replace(s, "[؟?!.,،]", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>"ar", "en" or null.</summary>
        public static string DetectLanguage(string text)
        {
            int ar = ArWord.Matches(text ?? "").Count, en = LatinWord.Matches(text ?? "").Count;
            if (ar == 0 && en == 0) return null;
            if (ar >= 2 || (ar >= 1 && ar >= en * 0.5f)) return "ar";
            return "en";
        }

        /// <summary>Explicit switch command ("ar" / "en") or null. Only short utterances whose purpose is the language itself.</summary>
        public static string DetectCommand(string text)
        {
            var t = Norm(text); if (t.Length == 0) return null;
            int words = t.Split(' ').Length; if (words > 12) return null;
            bool hasAr = HasArabicLang.IsMatch(t), hasEn = HasEnglishLang.IsMatch(t);
            if ((hasAr || hasEn) && (Verb.IsMatch(t) || words <= 3))
            {
                if (hasAr && !hasEn) return "ar";
                if (hasEn && !hasAr) return "en";
                return t.LastIndexOf("عربي") > new[] { t.LastIndexOf("انجليزي"), t.LastIndexOf("انكليزي"), t.LastIndexOf("english") }.Max() ? "ar" : "en";
            }
            var m = EnSwitch.Match(t);
            if (m.Success) { for (int g = 2; g <= 7; g++) { var v = m.Groups[g].Value; if (v == "arabic") return "ar"; if (v == "english") return "en"; } }
            return null;
        }

        public static string Ack(Persona p, string lang) => p == Persona.Patient
            ? (lang == "ar" ? "أكيد، خلينا نحكي عربي." : "Sure, we can speak English.")
            : (lang == "ar" ? "تمام، بنكمّل بالعربي." : "Of course, let's continue in English.");
    }
}
