using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MedSimAI.Surgery.AssetsEditor
{
    /// <summary>Minimal JSON reader for the asset-pipeline reports (keys may contain dots, so JsonUtility can't map them).</summary>
    public static class SimpleJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i); char c = s[i];
            if (c == '{') { var d = new Dictionary<string, object>(); i++; Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                while (true) { Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Value(s, ref i); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return d; } }
            if (c == '[') { var l = new List<object>(); i++; Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                while (true) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return l; } }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; char e = s[i]; if (e == 'u') { sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; } else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e); }
                else sb.Append(s[i]);
                i++;
            }
            i++; return sb.ToString();
        }
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static Vector3 V3(object o) { var l = Arr(o); return new Vector3((float)(double)l[0], (float)(double)l[1], (float)(double)l[2]); }
        public static float F(object o) => (float)(double)o;
    }
}
