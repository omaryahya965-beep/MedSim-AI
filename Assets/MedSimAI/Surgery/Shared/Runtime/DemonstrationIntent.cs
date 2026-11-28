using System.Text.RegularExpressions;

namespace MedSimAI.Surgery.Shared
{
    /// <summary>
    /// Deterministic recognition of "show me / do it in front of me / I want to learn from you" requests, in English,
    /// Palestinian Arabic and MSA, for both rooms. It is the safety net under the AI's own intent classification: the AI may
    /// miss a casual or imperfect phrasing («خبرتي ضعيفة… بدي إياكي تطبقي عملياً وأنا أتعلّم منك»), this never does.
    /// Engine-free (unit-tested). It only recognises the request; what is demonstrated, and how, is decided by the
    /// demonstrators from the authored scenarios.
    /// </summary>
    public static class DemonstrationIntent
    {
        public class Result
        {
            public bool demo;            // the student wants to SEE it done (not only hear it)
            public bool full;            // the whole procedure / examination, start to finish
            public bool stop;            // "stop, let me try now"
            public string topic = "";    // clinical: bp_cuff | stethoscope | pulse_oximeter | ecg | interview ; "" = the current/next step
            public override string ToString() => demo ? $"demo(full={full}, topic={topic})" : stop ? "stop" : "none";
        }

        static readonly Regex Demo = new Regex(
            @"(show me|show us|demonstrate|demonstration|\bdemo\b|do it (first|for me|in front of me)|you do it first|you go first|let me (watch|see you|see how)|i want to (see|watch) (you|how)|can i (watch|see) you|walk me through|take the lead|lead (the|this) (exam|examination|step|procedure)|teach me how|i (don.t|do not) (have enough information|know how)|no idea how|hands.on|\bpractical(ly)?\b|in practice|apply it|" +
            @"وريني|ورجيني|فرجيني|ورجينا|فرجينا|ورينا|اعرضيلي|اعرضلي|خليني اشوف|بدي اشوفك|اشوفك كيف|اشوف كيف|قدامي|\bاعمليها\b|\bاعملها\b|\bاعمليه\b|\bطبقي\b|\bتطبقي\b|\bطبقيها\b|\bتطبق\b|\bعمليا\b|بشكل عملي|\bعملي\b(?! ?ات)|علميني|\bعلمني\b|تعلميني|اتعلم منك|بتعلم منك|بدي اتعلم|ما عندي معلومات|مش عارف كيف|مش عارفه كيف|ما بعرف كيف|ما بعرف اعمل|خبرتي ضعيفه|خبرتي قليله|خبرتي كتير ضعيفه|خبره ضعيفه|خبره العمليه|بدي اياكي تعملي|بدي اياكي تروحي|بدي اياك تعمل|ابدئي انتي|ابداي انتي|ابدا انت)",
            RegexOptions.IgnoreCase);
        static readonly Regex Full = new Regex(
            @"((full|whole|entire|complete) (procedure|operation|surgery|exam|examination|process|thing)|the (procedure|operation|surgery)\b|from (the )?start to (the )?(finish|end)|beginning to end|start to finish|all (the )?steps|every step|" +
            @"(العمليه|الفحص|الجراحه) (كامله|كامل|كلها|كله)|كامل (العمليه|الفحص)|من البدايه|من الاول|للاخر|لاخر|كل الخطوات|كل العمليه|كل الفحص)", RegexOptions.IgnoreCase);
        static readonly Regex Stop = new Regex(
            @"(\bstop\b|that.s enough|enough|let me try|i.ll try|i will try|my turn|i can do it|" +
            @"\bخلص\b|\bبكفي\b|\bخلاص\b|\bوقفي\b|خليني اجرب|بدي اجرب|\bبجرب\b|\bدوري\b|بقدر اعملها)", RegexOptions.IgnoreCase);
        // exclusions: asking WHERE an instrument is, or asking the PATIENT to show where it hurts
        static readonly Regex WhereThing = new Regex(@"(\bwhere\b|وين|فين)", RegexOptions.IgnoreCase);
        static readonly Regex Hurts = new Regex(@"(hurt|pain|وجع|بوجع|يوجع|بيوجع|الم|ألم)", RegexOptions.IgnoreCase);

        static readonly (string topic, Regex re)[] Topics =
        {
            ("bp_cuff", new Regex(@"(blood pressure|\bbp\b|cuff|nibp|pressure|ضغط|الضغط|كفه|كفة|الكفه|الكفة|جهاز الضغط)", RegexOptions.IgnoreCase)),
            ("pulse_oximeter", new Regex(@"(oximeter|oxygen|saturation|sats|spo2|pulse ox|finger clip|اكسجين|الاكسجين|أكسجين|الأكسجين|نسبه الاكسجين|مقياس الاكسجين|الاصبع|اصبع|ملقط)", RegexOptions.IgnoreCase)),
            ("stethoscope", new Regex(@"(stethoscope|listen|auscultat|heart sounds|lungs|سماعه|السماعه|سماعة|السماعة|اسمع|تسمع|بسمع|القلب|الرئه|الرئة|الصدر)", RegexOptions.IgnoreCase)),
            ("ecg", new Regex(@"(\becg\b|\bekg\b|electrocardiogram|tracing|تخطيط|تخطيط القلب|ايكو|ecg)", RegexOptions.IgnoreCase)),
            ("interview", new Regex(@"(interview|history|ask (him|the patient)|talk to (him|the patient)|questions|speak (to|with) (him|the patient)|اسئله|أسئلة|اسئلة|تسالي|تسأليه|اسأل|اسال|تحكي معه|تحكي معاه|تحكي مع المريض|احكي معه|احكي معاه|تحكيله|مقابله|مقابلة|القصه المرضيه|التاريخ المرضي|كيف تحكي|كيف احكي)", RegexOptions.IgnoreCase)),
        };

        public static Result Detect(string utterance)
        {
            var r = new Result(); if (string.IsNullOrWhiteSpace(utterance)) return r;
            var u = Normalize(utterance);
            r.stop = Stop.IsMatch(u) && !Demo.IsMatch(u);
            if (r.stop) return r;
            bool demo = Demo.IsMatch(u);
            if (demo && WhereThing.IsMatch(u) && !Regex.IsMatch(u, @"(how|كيف)")) demo = false;   // "show me where the scalpel is" = locate
            if (demo && Hurts.IsMatch(u) && WhereThing.IsMatch(u)) demo = false;                   // "show me where it hurts" (to the patient)
            r.demo = demo;
            if (!demo) return r;
            r.full = Full.IsMatch(u);
            foreach (var (topic, re) in Topics) if (re.IsMatch(u)) { r.topic = topic; break; }
            return r;
        }

        /// <summary>Lower-case, no Arabic diacritics/tatweel, unified alef/ta-marbuta/ya forms.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s.ToLowerInvariant(), "[ً-ْـ]", "");
            s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه').Replace('ى', 'ي').Replace('’', '\'');
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
