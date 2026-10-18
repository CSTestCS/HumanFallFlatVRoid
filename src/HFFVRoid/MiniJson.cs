using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HFFVRoid
{
    /// <summary>
    /// Small JSON reader. Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string / bool / null. Enough for glTF / VRM headers.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            p.SkipWs();
            object v = p.ReadValue();
            return v;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string s) { this.s = s; }

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object ReadValue()
            {
                SkipWs();
                if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw new FormatException("Bad JSON token at " + i);
                i += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ReadString();
                    SkipWs();
                    if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            private List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                SkipWs();
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            private string ReadString()
            {
                if (s[i] != '"') throw new FormatException("Expected string at " + i);
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    char c = s[i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                return sb.ToString();
            }

            private double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw new FormatException("Bad JSON value at " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Helpers for walking parsed JSON safely.</summary>
    internal static class J
    {
        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static bool Has(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            return d != null && d.ContainsKey(key) && d[key] != null;
        }

        public static string Str(object o, string key, string def = null)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return def;
            object v;
            return d.TryGetValue(key, out v) && v is string ? (string)v : def;
        }

        public static double Num(object o, string key, double def = 0)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return def;
            object v;
            if (d.TryGetValue(key, out v))
            {
                if (v is double) return (double)v;
                if (v is bool) return (bool)v ? 1 : 0;
            }
            return def;
        }

        public static int Int(object o, string key, int def = -1)
        {
            return (int)Num(o, key, def);
        }

        public static bool Bool(object o, string key, bool def = false)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return def;
            object v;
            if (d.TryGetValue(key, out v) && v is bool) return (bool)v;
            return def;
        }

        public static float[] Floats(object o, string key)
        {
            var a = Arr(o, key);
            if (a == null) return null;
            var r = new float[a.Count];
            for (int k = 0; k < a.Count; k++) r[k] = a[k] is double ? (float)(double)a[k] : 0f;
            return r;
        }

        public static float[] Vec(object o, string key)
        {
            // VRM 0.x stores vectors as {"x":..,"y":..,"z":..}
            var d = Obj(o, key);
            if (d == null) return Floats(o, key);
            return new[] { (float)Num(d, "x"), (float)Num(d, "y"), (float)Num(d, "z") };
        }
    }
}
