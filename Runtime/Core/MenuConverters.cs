using System;
using System.Collections.Generic;
using System.Globalization;

namespace Threads.UiMenus
{
    /// <summary>
    /// Turns values into text and back, for saving (invariant culture, so a file saved in one
    /// language loads in another) and for converting edited values to a member's type. Built in:
    /// bool, the integer and floating point types, enums (names; flags as comma lists) and string.
    /// Add your own types with <see cref="Add{T}"/>; they are then edited as text and saved.
    /// </summary>
    public sealed class MenuConverters
    {
        readonly Dictionary<Type, (Func<object, string> write, Func<string, (bool ok, object value)> read)> _custom =
            new Dictionary<Type, (Func<object, string>, Func<string, (bool, object)>)>();

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void Add<T>(Func<T, string> write, Func<string, (bool ok, T value)> read)
        {
            _custom[typeof(T)] = (o => write((T)o), s => { var (ok, v) = read(s); return (ok, v); });
        }

        public bool IsCustom(Type type) => _custom.ContainsKey(type);

        public bool CanConvert(Type type) =>
            type == typeof(string) || type == typeof(bool) || type.IsEnum || IsNumber(type) || _custom.ContainsKey(type);

        public static bool IsNumber(Type t) => IsInteger(t) || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        public static bool IsInteger(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);

        public bool TryWrite(Type type, object value, out string text)
        {
            text = null;
            if (value == null) { text = ""; return type == typeof(string); }
            if (_custom.TryGetValue(type, out var c)) { text = c.write(value); return true; }
            switch (value)
            {
                case string s: text = s; return true;
                case bool b: text = b ? "true" : "false"; return true;
                case float f: text = f.ToString("R", Inv); return true;
                case double d: text = d.ToString("R", Inv); return true;
                case Enum e: text = e.ToString(); return true; // flags: "A, B"
                case IFormattable f2: text = f2.ToString(null, Inv); return true;
                default: return false;
            }
        }

        public bool TryRead(Type type, string text, out object value)
        {
            value = null;
            if (text == null) return false;
            if (_custom.TryGetValue(type, out var c))
            {
                var (ok, v) = c.read(text);
                value = v;
                return ok;
            }
            if (type == typeof(string)) { value = text; return true; }
            if (type == typeof(bool))
            {
                if (bool.TryParse(text.Trim(), out var b)) { value = b; return true; }
                return false;
            }
            if (type.IsEnum)
            {
                try
                {
                    var e = Enum.Parse(type, text.Trim(), ignoreCase: true);
                    if (!IsFlags(type) && !Enum.IsDefined(type, e)) return false;
                    value = e;
                    return true;
                }
                catch (ArgumentException) { return false; }
            }
            if (IsNumber(type))
            {
                if (!double.TryParse(text.Trim(), NumberStyles.Float, Inv, out var d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
                return TryCoerce(type, d, out value);
            }
            return false;
        }

        /// <summary>Converts an edited value (e.g. a slider's double, a cycler's index) to the
        /// member's type; integers round.</summary>
        public bool TryCoerce(Type type, object input, out object value)
        {
            value = null;
            if (input == null) return !type.IsValueType; // null is fine for strings and other references
            if (type.IsInstanceOfType(input)) { value = input; return true; }
            try
            {
                if (type.IsEnum)
                {
                    value = input is string s ? Enum.Parse(type, s, true) : Enum.ToObject(type, Convert.ToInt64(input, Inv));
                    return true;
                }
                if (IsInteger(type) && (input is float || input is double || input is decimal))
                {
                    var d = Convert.ToDouble(input, Inv);
                    value = Convert.ChangeType(Math.Round(d, MidpointRounding.AwayFromZero), type, Inv);
                    return true;
                }
                if (type == typeof(string)) { value = Convert.ToString(input, Inv); return true; }
                value = Convert.ChangeType(input, type, Inv);
                return true;
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException || e is ArgumentException)
            {
                return false;
            }
        }

        public static bool IsFlags(Type t) => t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false);
    }
}
