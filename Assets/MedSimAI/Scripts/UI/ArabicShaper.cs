using System.Collections.Generic;
using System.Text;

namespace MedSimAI.UI
{
    /// <summary>
    /// Minimal Arabic contextual shaping for TextMeshPro (which renders glyphs but does not shape).
    /// Converts logical Arabic text to Unicode Presentation Forms-B (isolated/final/initial/medial + lam-alef ligatures).
    /// Use the result with TMP's isRightToLeftText = true.
    /// </summary>
    public static class ArabicShaper
    {
        // letter -> (isolated, final, initial, medial); 0 = form not available (right-joining only)
        static readonly Dictionary<char, (char iso, char fin, char ini, char med)> Forms = new Dictionary<char, (char, char, char, char)>();
        static ArabicShaper()
        {
            void Dual(char c, int b) => Forms[c] = ((char)b, (char)(b + 1), (char)(b + 2), (char)(b + 3));
            void Right(char c, int b) => Forms[c] = ((char)b, (char)(b + 1), '\0', '\0');
            Forms['ء'] = ('ﺀ', '\0', '\0', '\0');
            Right('آ', 0xFE81); Right('أ', 0xFE83); Right('ؤ', 0xFE85); Right('إ', 0xFE87);
            Dual('ئ', 0xFE89); Right('ا', 0xFE8D); Dual('ب', 0xFE8F); Right('ة', 0xFE93);
            Dual('ت', 0xFE95); Dual('ث', 0xFE99); Dual('ج', 0xFE9D); Dual('ح', 0xFEA1); Dual('خ', 0xFEA5);
            Right('د', 0xFEA9); Right('ذ', 0xFEAB); Right('ر', 0xFEAD); Right('ز', 0xFEAF);
            Dual('س', 0xFEB1); Dual('ش', 0xFEB5); Dual('ص', 0xFEB9); Dual('ض', 0xFEBD); Dual('ط', 0xFEC1);
            Dual('ظ', 0xFEC5); Dual('ع', 0xFEC9); Dual('غ', 0xFECD); Dual('ف', 0xFED1); Dual('ق', 0xFED5);
            Dual('ك', 0xFED9); Dual('ل', 0xFEDD); Dual('م', 0xFEE1); Dual('ن', 0xFEE5); Dual('ه', 0xFEE9);
            Right('و', 0xFEED); Right('ى', 0xFEEF); Dual('ي', 0xFEF1);
        }

        static bool IsDiacritic(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';
        static bool IsArabicLetter(char c) => Forms.ContainsKey(c) || c == 'ـ';
        static bool JoinsLeft(char c) => c == 'ـ' || (Forms.TryGetValue(c, out var f) && f.ini != '\0');   // can connect to the following letter
        static bool JoinsRight(char c) => c == 'ـ' || (Forms.TryGetValue(c, out var f) && f.fin != '\0');  // can connect to the preceding letter

        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        /// <summary>Returns text with contextual presentation forms (logical order preserved).</summary>
        public static string Shape(string input)
        {
            if (!ContainsArabic(input)) return input;
            var sb = new StringBuilder(input.Length);
            int n = input.Length;
            for (int i = 0; i < n; i++)
            {
                char c = input[i];
                if (!IsArabicLetter(c)) { sb.Append(c); continue; }
                // neighbours, skipping diacritics
                int p = i - 1; while (p >= 0 && IsDiacritic(input[p])) p--;
                int q = i + 1; while (q < n && IsDiacritic(input[q])) q++;
                char prev = p >= 0 ? input[p] : '\0', next = q < n ? input[q] : '\0';

                // Lam-alef ligatures
                if (c == 'ل' && (next == 'آ' || next == 'أ' || next == 'إ' || next == 'ا'))
                {
                    bool joinPrev = prev != '\0' && JoinsLeft(prev);
                    int baseCode = next == 'آ' ? 0xFEF5 : next == 'أ' ? 0xFEF7 : next == 'إ' ? 0xFEF9 : 0xFEFB;
                    sb.Append((char)(baseCode + (joinPrev ? 1 : 0)));
                    for (int k = i + 1; k < q; k++) sb.Append(input[k]); // keep diacritics between
                    i = q; continue;
                }
                if (c == 'ـ') { sb.Append(c); continue; }
                var f = Forms[c];
                bool connectPrev = prev != '\0' && JoinsLeft(prev) && JoinsRight(c);
                bool connectNext = next != '\0' && JoinsLeft(c) && JoinsRight(next);
                char shaped = connectPrev && connectNext ? f.med : connectPrev ? f.fin : connectNext ? f.ini : f.iso;
                sb.Append(shaped == '\0' ? f.iso : shaped);
            }
            return sb.ToString();
        }

        /// <summary>Applies shaping + RTL to a TMP text component when the string contains Arabic.</summary>
        public static void Apply(TMPro.TMP_Text t, string text)
        {
            bool ar = ContainsArabic(text);
            t.isRightToLeftText = ar;
            t.text = ar ? Shape(text) : text;
        }
    }
}
