using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MedSimAI.Surgery.Shared;

namespace MedSimAI.Surgery.AI
{
    /// <summary>
    /// Controlled action registry for the surgical characters. The AI (or the offline intent parser) may only PROPOSE a
    /// catalogue action; <see cref="SurgeryActionValidator"/> decides deterministically from the authored scenario, the
    /// current objective and the instrument registry. Nothing outside this catalogue can be executed.
    /// </summary>
    public static class SurgeryActionCatalog
    {
        public const string None = "none", OtherPhysical = "other_physical";
        // assistant
        public const string ComeToStudent = "come_to_student", StandBesideStudent = "stand_beside_student", GoToInstrumentTable = "go_to_instrument_table",
            HandInstrument = "hand_instrument", LocateInstrument = "locate_instrument", ReturnToPosition = "return_to_position", Wait = "wait", LookAtStudent = "look_at_student";
        // surgeon
        public const string StepBack = "step_back", ReturnToObservation = "return_to_observation", LookAtSite = "look_at_site", ExplainObjective = "explain_objective";

        public static readonly string[] Assistant = { None, ComeToStudent, StandBesideStudent, GoToInstrumentTable, HandInstrument, LocateInstrument, ReturnToPosition, Wait, LookAtStudent, OtherPhysical };
        public static readonly string[] Surgeon = { None, ComeToStudent, StepBack, ReturnToObservation, LookAtStudent, LookAtSite, ExplainObjective, Wait, OtherPhysical };

        public static string[] For(string characterId) => characterId == SurgeryCharacterIds.Surgeon ? Surgeon : characterId == SurgeryCharacterIds.Assistant ? Assistant : new string[0];
        /// <summary>Actions that need an instrument id.</summary>
        public static bool NeedsInstrument(string action) => action == HandInstrument || action == LocateInstrument;
        /// <summary>Verbal-only actions (no actuator needed).</summary>
        public static bool IsVerbal(string action) => action == ExplainObjective;
    }

    public enum ActionStatus { None, Accepted, Denied, Clarify, RedirectToSupervisor }

    [Serializable]
    public class ProposedAction
    {
        public string characterId = "";
        public string action = SurgeryActionCatalog.None;
        public string instrumentId = "";        // id, alias or empty
        public bool forCurrentObjective;        // "the instrument I need" / "اللي بحتاجها"
        public string utterance = "";
    }

    [Serializable]
    public class ValidatedAction
    {
        public string characterId = "", action = SurgeryActionCatalog.None, instrumentId = "", reason = "", lineKey = "";
        public ActionStatus status = ActionStatus.None;
        public CharacterActionRequest ToRequest(string utterance = "") => new CharacterActionRequest { characterId = characterId, action = action, instrumentId = instrumentId, utterance = utterance };
        public override string ToString() => $"{characterId}:{action}({instrumentId}) {status} {reason}";
    }

    /// <summary>Context the validator needs; all read-only views from Shared interfaces.</summary>
    public class ActionContext
    {
        public SurgicalScenarioData Scenario;
        public string CurrentObjectiveId = "";
        public IInstrumentRegistry Instruments;
    }

    public static class SurgeryActionValidator
    {
        public static ValidatedAction Validate(ProposedAction p, ActionContext ctx)
        {
            var v = new ValidatedAction { characterId = p?.characterId ?? "", action = p?.action ?? SurgeryActionCatalog.None };
            if (p == null || string.IsNullOrEmpty(v.action) || v.action == SurgeryActionCatalog.None) { v.status = ActionStatus.None; v.action = SurgeryActionCatalog.None; return v; }
            if (v.characterId != SurgeryCharacterIds.Surgeon && v.characterId != SurgeryCharacterIds.Assistant) return Deny(v, "unknown_character", "clarify_other");

            // Instrument handling is the assistant's job, whoever was addressed ("دكتور، ممكن تعطيني الأداة؟").
            if (SurgeryActionCatalog.NeedsInstrument(v.action)) v.characterId = SurgeryCharacterIds.Assistant;

            if (!SurgeryActionCatalog.For(v.characterId).Contains(v.action)) return Clarify(v, "not_in_catalogue", "clarify_other");
            if (v.action == SurgeryActionCatalog.OtherPhysical) return Clarify(v, "not_in_catalogue", "clarify_other");
            var s = ctx?.Scenario;
            if (s != null && s.assistantPolicy.deniedActions.Contains(v.action)) return Deny(v, "scenario_policy", "denied_policy");

            if (SurgeryActionCatalog.NeedsInstrument(v.action))
            {
                var ids = InstrumentResolver.Resolve(p.instrumentId, p.utterance, s);
                if (ids.Count == 0)
                {
                    if (p.forCurrentObjective) { v.status = ActionStatus.RedirectToSupervisor; v.reason = "needs_hint"; v.lineKey = "ask_surgeon"; return v; }
                    return Clarify(v, "which_instrument", "clarify_instrument");
                }
                if (ids.Count > 1) { v.instrumentId = string.Join("|", ids); return Clarify(v, "ambiguous_instrument", "clarify_instrument"); }
                v.instrumentId = ids[0];
                if (s != null && s.Instrument(v.instrumentId) == null) return Deny(v, "not_in_scenario", "not_available");
                var holder = ctx?.Instruments?.HolderOf(v.instrumentId) ?? InstrumentHolder.OnTable;
                if (holder == InstrumentHolder.Missing) return Deny(v, "instrument_unavailable", "not_available");
                if (v.action == SurgeryActionCatalog.HandInstrument)
                {
                    if (holder == InstrumentHolder.Player) return Deny(v, "already_holding", "already_holding");
                    if (holder == InstrumentHolder.OnPatient) return Deny(v, "in_use", "in_use");
                    if (s != null && !s.assistantPolicy.handoverTools.Contains(v.instrumentId)) return Deny(v, "handover_not_allowed", "denied_policy");
                    if (s != null && s.assistantPolicy.handoverOnlyForCurrentObjective)
                    {
                        var obj = s.Objective(ctx.CurrentObjectiveId);
                        if (obj != null && !obj.requiredTools.Contains(v.instrumentId)) return Deny(v, "not_needed_now", "not_needed_now");
                    }
                }
            }
            v.status = ActionStatus.Accepted; v.lineKey = AckKey(v.action);
            return v;
        }

        static string AckKey(string a) => a == SurgeryActionCatalog.HandInstrument ? "ack_hand" : a == SurgeryActionCatalog.LocateInstrument ? "ack_locate" : a == SurgeryActionCatalog.Wait ? "ack_wait" : a == SurgeryActionCatalog.ExplainObjective ? "" : "ack_move";
        static ValidatedAction Deny(ValidatedAction v, string reason, string line) { v.status = ActionStatus.Denied; v.reason = reason; v.lineKey = line; return v; }
        static ValidatedAction Clarify(ValidatedAction v, string reason, string line) { v.status = ActionStatus.Clarify; v.reason = reason; v.lineKey = line; return v; }
    }

    /// <summary>Maps an id, alias or free utterance to scenario instrument ids (exact id first, then names/aliases in en/ar).</summary>
    public static class InstrumentResolver
    {
        public static List<string> Resolve(string idOrAlias, string utterance, SurgicalScenarioData s)
        {
            var result = new List<string>();
            if (!string.IsNullOrEmpty(idOrAlias) && idOrAlias != "none")
            {
                if (s == null || s.Instrument(idOrAlias) != null) { result.Add(idOrAlias); return result; }
                result.AddRange(Match(idOrAlias, s));
                if (result.Count > 0) return result;
            }
            if (!string.IsNullOrEmpty(utterance) && s != null) result.AddRange(Match(utterance, s));
            return result.Distinct().ToList();
        }

        static readonly HashSet<string> GenericWords = new HashSet<string> { "surgical", "instrument", "اداه", "جراحي", "this", "that", "need", "with" };

        static IEnumerable<string> Match(string text, SurgicalScenarioData s)
        {
            var t = SurgeryText.Normalize(text);
            // Longest name/alias wins, so "femoral cutting guide" beats "guide" and "tibial guide" beats "guide".
            var hits = new List<(string id, int len)>();
            foreach (var i in s.instruments)
                foreach (var name in new[] { i.name.en, i.name.ar }.Concat(i.aliases))
                {
                    var n = SurgeryText.Normalize(name);
                    if (n.Length >= 3 && SurgeryText.ContainsWord(t, n)) hits.Add((i.id, n.Length));
                }
            if (hits.Count == 0)
            {
                // Partial: a distinctive word of a name ("guide", "دليل", "skin") — may match several (→ clarification).
                var words = new HashSet<string>(t.Split(' '));
                var partial = s.instruments.Where(i => new[] { i.name.en, i.name.ar }.SelectMany(n => SurgeryText.Normalize(n).Split(' '))
                    .Any(w => w.Length >= 4 && !GenericWords.Contains(w) && (words.Contains(w) || words.Contains("ال" + w)))).Select(i => i.id).ToList();
                return partial;
            }
            int best = hits.Max(h => h.len);
            // Keep every instrument whose best match is this long (genuinely ambiguous, e.g. just "guide").
            return hits.GroupBy(h => h.id).Where(g => g.Max(x => x.len) == best).Select(g => g.Key);
        }
    }

    public static class SurgeryText
    {
        static readonly Regex Diacritics = new Regex("[ً-ْـ]");
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Diacritics.Replace(s.ToLowerInvariant(), "");
            s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه').Replace('ى', 'ي');
            s = Regex.Replace(s, @"[^\p{L}\p{N}\s\.]", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
        /// <summary>Whole-word (or Arabic prefix-tolerant: ال/بال/وال) containment.</summary>
        public static bool ContainsWord(string haystack, string needle)
        {
            var h = " " + haystack + " ";
            if (h.Contains(" " + needle + " ")) return true;
            foreach (var pre in new[] { "ال", "بال", "وال", "لل", "ب", "و" }) if (h.Contains(" " + pre + needle + " ")) return true;
            if (needle.StartsWith("ال") && h.Contains(" " + needle.Substring(2) + " ")) return true;
            return false;
        }
    }

    /// <summary>
    /// Deterministic intent parser: the offline fallback and safety net when the AI is unavailable or returns nothing.
    /// Covers the scenario's supported request phrasings in English and Palestinian Arabic.
    /// </summary>
    public static class SurgeryIntentParser
    {
        public class Intent
        {
            public string addressed = "";          // surgeon | assistant | "" (unknown)
            public ProposedAction action = new ProposedAction();
            public bool helpRequest, explainRequest;
            public string languageCommand = "";    // "ar" | "en" | ""
            public override string ToString() => $"to={addressed} action={action.action}({action.instrumentId}) help={helpRequest} explain={explainRequest} lang={languageCommand}";
        }

        static readonly Regex AddrSurgeon = new Regex(@"(دكتور|دكتوره|الدكتوره|دكتورة|doctor|\bdr\b|لينا|lina|haddad|حداد)", RegexOptions.IgnoreCase);
        static readonly Regex AddrAssistant = new Regex(@"(سامي|sami|assistant|مساعد)", RegexOptions.IgnoreCase);
        static readonly Regex LangAr = new Regex(@"(احكي|احكيلي|تحكي|حكي|خلينا نحكي)\s*(عربي|بالعربي)|speak arabic|in arabic", RegexOptions.IgnoreCase);
        static readonly Regex LangEn = new Regex(@"(احكي|تحكي|حكي)\s*(انجليزي|انكليزي|بالانجليزي)|speak english|in english", RegexOptions.IgnoreCase);
        static readonly Regex Give = new Regex(@"(عطيني|اعطيني|تعطيني|ناولني|تناولني|جيبلي|بدي\s+ال|pass me|hand me|give me|can i have|i need the|بحتاج\s+ال)", RegexOptions.IgnoreCase);
        static readonly Regex Where = new Regex(@"(وين|فين|where('s| is| are)?|which one|اي وحده|أي وحدة)", RegexOptions.IgnoreCase);
        static readonly Regex Needed = new Regex(@"(اللي بحتاجها|اللي بدي اياها|اللي لازمني|المطلوبه|المطلوبة|i need|the right one|needed|for this step)", RegexOptions.IgnoreCase);
        static readonly Regex Help = new Regex(@"(ساعدني|تساعدني|مساعده|مساعدة|help|stuck|مش عارف|مش فاهم|i don.t know|what (do|should) i do)", RegexOptions.IgnoreCase);
        static readonly Regex Explain = new Regex(@"(وضحلي|توضحلي|اشرحلي|تشرحلي|شو المطلوب|شو لازم|explain|what('s| is) (required|the goal|this step)|what am i supposed)", RegexOptions.IgnoreCase);
        static readonly Regex Come = new Regex(@"(تعال|تعالي|قرب|اقرب|come (here|over)|come closer)", RegexOptions.IgnoreCase);
        static readonly Regex Beside = new Regex(@"(جنبي|حدي|beside me|next to me|move beside)", RegexOptions.IgnoreCase);
        static readonly Regex Wait = new Regex(@"(استنى|استني|ستنى|لحظه|لحظة|wait|hold on|one moment)", RegexOptions.IgnoreCase);
        static readonly Regex Back = new Regex(@"(ارجع|رجعي|ارجعي|ابعد|بعّد|step back|go back|move back|give me space)", RegexOptions.IgnoreCase);
        static readonly Regex Table = new Regex(@"(الطاوله|الطاولة|instrument table|to the table)", RegexOptions.IgnoreCase);

        public static Intent Parse(string utterance, SurgicalScenarioData s = null)
        {
            var it = new Intent(); var u = utterance ?? ""; var n = SurgeryText.Normalize(u);
            it.action.utterance = u;
            if (LangAr.IsMatch(n) || LangAr.IsMatch(u)) it.languageCommand = "ar";
            else if (LangEn.IsMatch(n) || LangEn.IsMatch(u)) it.languageCommand = "en";
            it.addressed = AddrAssistant.IsMatch(u) ? SurgeryCharacterIds.Assistant : AddrSurgeon.IsMatch(u) ? SurgeryCharacterIds.Surgeon : "";
            var instrument = s != null ? InstrumentResolver.Resolve("", u, s) : new List<string>();
            bool mentionsTool = instrument.Count > 0 || Regex.IsMatch(n, @"(الاداه|اداه|الادوات|instrument|tool)");
            bool needed = Needed.IsMatch(n) || Needed.IsMatch(u);
            string who = it.addressed;

            if (Give.IsMatch(n) || Give.IsMatch(u) || (mentionsTool && Regex.IsMatch(n, @"(ممكن|بتقدر|can you|could you)") && !Where.IsMatch(n)))
                Set(it, SurgeryCharacterIds.Assistant, SurgeryActionCatalog.HandInstrument, instrument, needed);
            else if (Where.IsMatch(n) && mentionsTool)
                Set(it, SurgeryCharacterIds.Assistant, SurgeryActionCatalog.LocateInstrument, instrument, needed);
            else if (Explain.IsMatch(n) || Explain.IsMatch(u)) { it.explainRequest = true; Set(it, SurgeryCharacterIds.Surgeon, SurgeryActionCatalog.ExplainObjective, null, false); }
            else if (Beside.IsMatch(n)) Set(it, Or(who, SurgeryCharacterIds.Assistant), Or(who, SurgeryCharacterIds.Assistant) == SurgeryCharacterIds.Assistant ? SurgeryActionCatalog.StandBesideStudent : SurgeryActionCatalog.ComeToStudent, null, false);
            else if (Come.IsMatch(n)) Set(it, Or(who, SurgeryCharacterIds.Surgeon), SurgeryActionCatalog.ComeToStudent, null, false);
            else if (Table.IsMatch(n)) Set(it, SurgeryCharacterIds.Assistant, SurgeryActionCatalog.GoToInstrumentTable, null, false);
            else if (Back.IsMatch(n)) Set(it, Or(who, SurgeryCharacterIds.Surgeon), Or(who, SurgeryCharacterIds.Surgeon) == SurgeryCharacterIds.Surgeon ? SurgeryActionCatalog.StepBack : SurgeryActionCatalog.ReturnToPosition, null, false);
            else if (Wait.IsMatch(n)) Set(it, Or(who, SurgeryCharacterIds.Assistant), SurgeryActionCatalog.Wait, null, false);

            if (Help.IsMatch(n) || Help.IsMatch(u)) { it.helpRequest = true; if (it.action.action == SurgeryActionCatalog.None) it.action.characterId = SurgeryCharacterIds.Surgeon; }
            if (string.IsNullOrEmpty(it.action.characterId)) it.action.characterId = Or(who, SurgeryCharacterIds.Surgeon);
            if (string.IsNullOrEmpty(it.addressed)) it.addressed = it.action.characterId;
            return it;
        }

        static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;
        static void Set(Intent it, string who, string action, List<string> instrument, bool needed)
        {
            it.action.characterId = who; it.action.action = action; it.action.forCurrentObjective = needed;
            it.action.instrumentId = instrument != null && instrument.Count == 1 ? instrument[0] : "";
        }
    }

    /// <summary>Approved short lines for action outcomes (never claims completion; spoken before/while acting).</summary>
    public static class SurgeryActionLines
    {
        static readonly Dictionary<string, (string en, string ar)> L = new Dictionary<string, (string, string)>
        {
            ["ack_move"] = ("Sure.", "أكيد."),
            ["ack_wait"] = ("Of course, I'll wait.", "أكيد، مستني."),
            ["ack_hand"] = ("Here, take the {0}.", "تفضّل، هاي الأداة: {0}."),
            ["ack_locate"] = ("The {0} is on the instrument table; I'll show you.", "الأداة ({0}) على طاولة الأدوات، تعال أفرجيك."),
            ["clarify_instrument"] = ("Which instrument do you need?", "أي أداة بدك بالزبط؟"),
            ["clarify_other"] = ("Sorry, what would you like me to do?", "معلش، شو بدك أعمل بالزبط؟"),
            ["denied_policy"] = ("I can't do that in this simulation.", "ما بقدر أعمل هيك بهالمحاكاة."),
            ["not_available"] = ("We don't have that instrument here.", "هاي الأداة مش موجودة عنا هون."),
            ["already_holding"] = ("You're already holding it.", "هي معك أصلاً."),
            ["in_use"] = ("That one is in use right now.", "هاي مستعملة هلأ."),
            ["not_needed_now"] = ("You won't need that one for this step.", "مش رح تحتاجها بهالخطوة."),
            ["ask_surgeon"] = ("Dr. Haddad, could you guide us on this step?", "دكتورة لينا، بتقدري توجهينا بهالخطوة؟"),
            ["handoff_not_taken"] = ("I'll put it back on the table for now.", "رح أرجعها عالطاولة هلأ."),
            ["action_unavailable"] = ("Sorry, I can't get there right now.", "معلش، ما بقدر أوصل هلأ."),
        };
        public static string Get(string key, string lang, string arg = "")
        {
            if (string.IsNullOrEmpty(key) || !L.TryGetValue(key, out var v)) return "";
            var t = lang == "ar" ? v.ar : v.en;
            return t.Contains("{0}") ? string.Format(t, arg) : t;
        }
        public static IEnumerable<string> All(string lang) => L.Values.Select(v => lang == "ar" ? v.ar : v.en);
    }
}
