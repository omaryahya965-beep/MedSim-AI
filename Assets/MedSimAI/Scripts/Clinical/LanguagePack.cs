using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MedSimAI.Clinical
{
    /// <summary>
    /// Separate, clinically reviewable translation of the approved scenario text (Resources/Scenarios/{id}.{lang}.json).
    /// Translation is a presentation layer over approved English content: it maps each approved English string to its
    /// translation. Untranslated strings stay in English (never machine-translated at runtime).
    /// </summary>
    public class LanguagePack
    {
        public string Language { get; private set; } = "en";
        public string Status { get; private set; } = "SOURCE";
        public bool IsReviewed => Status == "APPROVED_TRANSLATION";
        readonly List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>(); // english -> translated, longest first

        [Serializable] class Entry { public string key, value; }

        public static LanguagePack Load(ScenarioData s, string language)
        {
            var pack = new LanguagePack { Language = language };
            if (language == "en") return pack;
            var ta = Resources.Load<TextAsset>($"Scenarios/{s.scenarioId}.{language}");
            if (!ta) { pack.Language = "en"; return pack; }
            var root = MiniJson.Parse(ta.text) as Dictionary<string, object>;
            pack.Status = root.TryGetValue("status", out var st) ? st as string : "DRAFT";
            Dictionary<string, object> D(string k) => root.TryGetValue(k, out var v) ? v as Dictionary<string, object> : new Dictionary<string, object>();
            var facts = D("facts"); foreach (var f in s.historyFacts) if (facts.TryGetValue(f.id, out var t)) pack.Add(f.answer, t as string);
            var unk = D("unknownAnswers"); for (int i = 0; i < s.unknownAnswers.Length; i++) if (unk.TryGetValue(i.ToString(), out var t)) pack.Add(s.unknownAnswers[i], t as string);
            var il = D("instructorLines"); var L = s.instructorLines;
            foreach (var (k, en) in new[] { ("welcome", L.welcome), ("tutorial", L.tutorial), ("historyDone", L.historyDone), ("examDone", L.examDone), ("ecgShown", L.ecgShown), ("reasoningPrompt", L.reasoningPrompt), ("escalationAck", L.escalationAck), ("debriefIntro", L.debriefIntro), ("outOfScope", L.outOfScope), ("noHintAvailable", L.noHintAvailable), ("onboardingRole", L.onboardingRole), ("onboardingInstruments", L.onboardingInstruments) })
                if (il.TryGetValue(k, out var t) && !string.IsNullOrEmpty(en)) pack.Add(en, t as string);
            var hints = D("hints"); foreach (var h in s.hints) if (hints.TryGetValue($"{h.stage}|{h.order}", out var t)) pack.Add(h.text, t as string);
            var scripted = D("scripted");
            if (scripted.TryGetValue("InstructorReasoningFollowUp", out var a)) pack.Add(AI.ScriptedLines.InstructorReasoningFollowUp, a as string);
            if (scripted.TryGetValue("InstructorGeneric", out var b)) pack.Add(AI.ScriptedLines.InstructorGeneric, b as string);
            var inv = D("investigations");
            foreach (var i in s.investigations)
                if (inv.TryGetValue(i.id, out var nm))
                {
                    pack.Add(AI.ScriptedLines.InvestigationArranged(i.name), $"تم طلب {nm}. النتيجة معروضة على الشاشة.");
                    pack.Add(AI.ScriptedLines.InvestigationUnavailable(i.name), $"{nm} غير متاح في هذا السيناريو.");
                }
            var ui = D("ui"); if (ui.TryGetValue("banner", out var bn)) pack.Add(s.ValidationBanner, bn as string);
            pack.pairs.Sort((x, y) => y.Key.Length.CompareTo(x.Key.Length));
            return pack;
        }

        void Add(string en, string tr) { if (!string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(tr)) pairs.Add(new KeyValuePair<string, string>(en, tr)); }

        public bool Has(string english) => pairs.Any(p => p.Key == english);

        /// <summary>Replaces every approved English segment with its translation (exact, longest-first).</summary>
        public string Localize(string text)
        {
            if (Language == "en" || string.IsNullOrEmpty(text)) return text;
            foreach (var p in pairs) if (text.Contains(p.Key)) text = text.Replace(p.Key, p.Value);
            return text;
        }
    }

    /// <summary>Tiny JSON reader (objects, arrays, strings, numbers, bools) — JsonUtility cannot read dictionaries.</summary>
    public static class MiniJson
    {
        public static object Parse(string json) { int i = 0; return Value(json, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (s[i] == '﻿') { i++; Ws(s, ref i); }
            switch (s[i])
            {
                case '{': { var d = new Dictionary<string, object>(); i++; Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                        while (true) { Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Value(s, ref i); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return d; } }
                case '[': { var l = new List<object>(); i++; Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                        while (true) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return l; } }
                case '"': return Str(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: { int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++; return double.Parse(s.Substring(st, i - st), System.Globalization.CultureInfo.InvariantCulture); }
            }
        }
        static string Str(string s, ref int i)
        {
            var sb = new System.Text.StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++; char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++; return sb.ToString();
        }
    }
}
