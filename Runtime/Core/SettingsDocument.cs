using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Threads.UiMenus
{
    /// <summary>Where settings are kept: one text document per key (one per settings object).</summary>
    public interface ISettingsStore
    {
        bool TryLoad(string key, out string text);
        void Save(string key, string text);
    }

    /// <summary>Keeps settings in memory (tests, or a game that saves them its own way).</summary>
    public sealed class InMemorySettingsStore : ISettingsStore
    {
        readonly Dictionary<string, string> _texts = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> Texts => _texts;
        public int Saves { get; private set; }
        public bool TryLoad(string key, out string text) => _texts.TryGetValue(key, out text);

        public void Save(string key, string text)
        {
            _texts[key] = text;
            Saves++;
        }
    }

    /// <summary>
    /// The saved form of a settings object:
    /// <c>{ "version": 1, "values": { "Master": "0.8", "Graphics.Shadows": "true" } }</c>. Values
    /// are text in the invariant culture, keyed by member path (or <c>[SettingKey]</c>). Reading is
    /// forgiving: missing keys keep the object's own value, earlier names from
    /// <c>[FormerlySerializedAs]</c> are found, and unknown keys are ignored.
    /// </summary>
    public static class SettingsDocument
    {
        public const int Version = 1;

        /// <summary>The saved values of <paramref name="target"/> as a document.</summary>
        public static string Write(MenuSchema schema, object target, MenuConverters converters)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in schema.Values)
            {
                if (!e.Saved) continue;
                var v = Read(target, e);
                if (converters.TryWrite(e.ValueType, v, out var text)) values[e.Key] = text;
            }
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": ").Append(Version.ToString(CultureInfo.InvariantCulture)).Append(",\n  \"values\": {");
            var first = true;
            foreach (var kv in values)
            {
                sb.Append(first ? "\n    " : ",\n    ");
                first = false;
                Json.WriteString(sb, kv.Key);
                sb.Append(": ");
                Json.WriteString(sb, kv.Value);
            }
            sb.Append(first ? "}\n}\n" : "\n  }\n}\n");
            return sb.ToString();
        }

        /// <summary>Sets <paramref name="target"/>'s members from a document (through their setters,
        /// so side effects happen as at Apply). Returns problems found; they do not stop the rest.</summary>
        public static List<string> Read(string text, MenuSchema schema, object target, MenuConverters converters)
        {
            var problems = new List<string>();
            Dictionary<string, object> root;
            try { root = Json.Parse(text) as Dictionary<string, object>; }
            catch (FormatException e) { problems.Add($"{schema.Key}: not a settings document ({e.Message})."); return problems; }
            if (root == null || !(root.TryGetValue("values", out var raw) && raw is Dictionary<string, object> values))
            {
                problems.Add($"{schema.Key}: no \"values\" in the settings document.");
                return problems;
            }
            foreach (var e in schema.Values)
            {
                if (!e.Saved) continue;
                if (!TryFind(values, e, out var found)) continue;
                if (!converters.TryRead(e.ValueType, found, out var v))
                {
                    problems.Add($"{schema.Key}: '{found}' is not a valid {e.ValueType.Name} for {e.Key}; kept {Read(target, e)}.");
                    continue;
                }
                Write(target, e, v);
            }
            return problems;
        }

        /// <summary>Loads <paramref name="target"/> from <paramref name="store"/> (under its schema's key),
        /// if saved. Returns problems found.</summary>
        public static List<string> Load(ISettingsStore store, MenuSchemaCache schemas, object target)
        {
            var schema = schemas.Get(target.GetType());
            return store.TryLoad(schema.Key, out var text) ? Read(text, schema, target, schemas.Converters) : new List<string>();
        }

        static bool TryFind(Dictionary<string, object> values, MenuEntry e, out string text)
        {
            text = null;
            if (values.TryGetValue(e.Key, out var v) || TryOld(values, e, out v))
            {
                text = v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
                return true;
            }
            return false;
        }

        static bool TryOld(Dictionary<string, object> values, MenuEntry e, out object v)
        {
            foreach (var old in e.OldKeys)
                if (values.TryGetValue(old, out v)) return true;
            v = null;
            return false;
        }

        static object Read(object target, MenuEntry e)
        {
            var owner = target;
            var chain = new List<MenuEntry>();
            for (var x = e; x != null; x = x.Parent) chain.Insert(0, x);
            for (var i = 0; i < chain.Count - 1 && owner != null; i++) owner = chain[i].GetFrom(owner);
            return owner == null ? null : e.GetFrom(owner);
        }

        static void Write(object target, MenuEntry e, object value)
        {
            var chain = new List<MenuEntry>();
            for (var x = e; x != null; x = x.Parent) chain.Insert(0, x);
            MenuSession.WritePath(target, chain, 0, value);
        }
    }

    /// <summary>Just enough JSON for settings documents (objects, arrays, strings, numbers, bools, null).</summary>
    internal static class Json
    {
        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static object Parse(string text)
        {
            var i = 0;
            var v = Value(text ?? throw new FormatException("no text"), ref i);
            Space(text, ref i);
            if (i != text.Length) throw new FormatException($"unexpected '{text[i]}' at {i}");
            return v;
        }

        static object Value(string s, ref int i)
        {
            Space(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            switch (s[i])
            {
                case '{': return Object(s, ref i);
                case '[': return Array(s, ref i);
                case '"': return String(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return Number(s, ref i);
            }
        }

        static Dictionary<string, object> Object(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Space(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"expected a name at {i}");
                var key = String(s, ref i);
                Space(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
                i++;
                d[key] = Value(s, ref i);
                Space(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException($"expected ',' or '}}' at {i}");
            }
        }

        static List<object> Array(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i));
                Space(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException($"expected ',' or ']' at {i}");
            }
        }

        static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                var e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        static object Number(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"unexpected '{s[i]}' at {i}");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException($"expected {word} at {i}");
            i += word.Length;
        }

        static void Space(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
