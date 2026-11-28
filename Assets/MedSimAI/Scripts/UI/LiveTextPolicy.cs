using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace MedSimAI.UI
{
    /// <summary>
    /// Owner's request (2026-11-28): the live experience shows no "DRAFT / NOT CLINICALLY VALIDATED" ribbons or labels.
    /// Scenes generated before that request still carry such text on world props (lobby sign, notice-board sheet, bedside
    /// chart, poster); this removes it at start-up without regenerating the scenes. The validation status itself is unchanged
    /// and stays documented (CLINICAL_REVIEW.md, scenario files, assessment records); only the on-screen wording goes.
    /// </summary>
    public static class LiveTextPolicy
    {
        static readonly Regex DraftLine = new Regex(@"(?im)^.*(DRAFT|NOT CLINICALLY|not clinically|clinically validated|مسودة|غير معتمدة).*$\n?");
        static readonly Regex DraftTail = new Regex(@"\s*[·•—-]\s*DRAFT\s*[—-]\s*not clinically validated", RegexOptions.IgnoreCase);

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var t = DraftTail.Replace(s, "");
            if (t.Trim() == "DRAFT") return "SIMULATION";
            t = DraftLine.Replace(t, "");
            return t.TrimEnd('\n', ' ');
        }

        /// <summary>Cleans every text in the loaded scenes (inactive ones too). Returns how many were changed.</summary>
        public static int ScrubScene()
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var c = Clean(t.text);
                if (c != t.text) { t.text = c; n++; }
            }
            return n;
        }
    }
}
