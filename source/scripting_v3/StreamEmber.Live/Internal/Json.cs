//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StreamEmber.Live.Internal
{
    /// <summary>
    /// Minimal JSON without external dependencies (scripts may ship their own Newtonsoft.Json).
    /// Object = Dictionary&lt;string, object&gt; (ordinal), array = List&lt;object&gt;, string, double, bool, null.
    /// </summary>
    internal static class Json
    {
        private const int MaxDepth = 64;

        public static Dictionary<string, object> NewObject() => new Dictionary<string, object>(StringComparer.Ordinal);

        public static object Parse(string text)
        {
            var parser = new Parser(text ?? string.Empty);
            parser.SkipWhitespace();
            object value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Unexpected characters after the JSON value.");
            return value;
        }

        /// <summary>Parses an object; empty text gives an empty object, any other root type is an error.</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return NewObject();
            return Parse(text) as Dictionary<string, object> ?? throw new FormatException("The JSON root is not an object.");
        }

        // ─── Access ─────────────────────────────────────────────────────────

        public static Dictionary<string, object> Obj(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;

        public static List<object> Arr(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out object value) ? value as List<object> : null;

        /// <summary>Scalar as invariant text; missing, null, object or array gives "".</summary>
        public static string Text(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out object value) ? ScalarText(value) : string.Empty;

        public static string ScalarText(object value)
        {
            switch (value)
            {
                case string text: return text;
                case bool flag: return flag ? "true" : "false";
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                default: return string.Empty;
            }
        }

        /// <summary>Number (or numeric text); false when missing, not a number or not finite.</summary>
        public static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (value is double d) number = d;
            else if (value is int i) number = i;
            else if (value is long l) number = l;
            else if (!(value is string text) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        public static long Long(Dictionary<string, object> obj, string key, long fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out object value) || !TryNumber(value, out double number)) return fallback;
            if (number >= long.MaxValue) return long.MaxValue;
            if (number <= long.MinValue) return long.MinValue;
            return (long)Math.Round(number);
        }

        public static bool Bool(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out object value) && value is bool flag && flag;

        // ─── Copy / merge ───────────────────────────────────────────────────

        public static object Clone(object value)
        {
            switch (value)
            {
                case Dictionary<string, object> obj:
                    var copy = NewObject();
                    foreach (KeyValuePair<string, object> pair in obj) copy[pair.Key] = Clone(pair.Value);
                    return copy;
                case List<object> list:
                    var items = new List<object>(list.Count);
                    foreach (object item in list) items.Add(Clone(item));
                    return items;
                default:
                    return value;
            }
        }

        public static Dictionary<string, object> CloneObject(Dictionary<string, object> obj) =>
            obj == null ? NewObject() : (Dictionary<string, object>)Clone(obj);

        /// <summary>Deep merge into a copy of <paramref name="baseObject"/>: overlay wins, arrays are replaced, nulls ignored.</summary>
        public static Dictionary<string, object> Merge(Dictionary<string, object> baseObject, Dictionary<string, object> overlay)
        {
            Dictionary<string, object> result = CloneObject(baseObject);
            if (overlay != null) MergeInto(result, overlay);
            return result;
        }

        private static void MergeInto(Dictionary<string, object> target, Dictionary<string, object> overlay)
        {
            foreach (KeyValuePair<string, object> pair in overlay)
            {
                if (pair.Value == null) continue;
                if (pair.Value is Dictionary<string, object> child && target.TryGetValue(pair.Key, out object existing)
                    && existing is Dictionary<string, object> existingChild)
                    MergeInto(existingChild, child);
                else
                    target[pair.Key] = Clone(pair.Value);
            }
        }

        // ─── Write ──────────────────────────────────────────────────────────

        public static string Write(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case double number:
                    WriteNumber(builder, number);
                    break;
                case int number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case long number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case Dictionary<string, object> obj:
                    builder.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in obj)
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        WriteValue(builder, pair.Value);
                    }
                    builder.Append('}');
                    break;
                case List<object> list:
                    builder.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) builder.Append(',');
                        WriteValue(builder, list[i]);
                    }
                    builder.Append(']');
                    break;
                default:
                    WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteNumber(StringBuilder builder, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) builder.Append("null");
            else if (Math.Abs(number) < 1e15 && Math.Floor(number) == number) builder.Append(((long)number).ToString(CultureInfo.InvariantCulture));
            else builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text ?? string.Empty)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        // ─── Parse ──────────────────────────────────────────────────────────

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
            }

            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
                // A UTF-8 BOM read as text.
                if (_index == 0 && _text.Length > 0 && _text[0] == '﻿')
                {
                    _index = 1;
                    SkipWhitespace();
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON is nested too deeply");
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = _text[_index];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = NewObject();
                _index++;
                SkipWhitespace();
                if (Peek('}')) return result;
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"') throw Error("Expected a property name");
                    string key = ReadString();
                    SkipWhitespace();
                    Consume(':');
                    SkipWhitespace();
                    result[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Peek('}')) return result;
                    Consume(',');
                }
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                _index++;
                SkipWhitespace();
                if (Peek(']')) return result;
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek(']')) return result;
                    Consume(',');
                }
            }

            private string ReadString()
            {
                _index++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _text[_index++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = _text[_index++];
                    switch (e)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length
                                || !int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("Invalid \\u escape");
                            builder.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error("Invalid escape '\\" + e + "'");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _index;
                while (_index < _text.Length && "+-0123456789.eE".IndexOf(_text[_index]) >= 0) _index++;
                string token = _text.Substring(start, _index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error("Invalid number '" + token + "'");
                return value;
            }

            private bool Peek(char c)
            {
                if (_index < _text.Length && _text[_index] == c)
                {
                    _index++;
                    return true;
                }
                return false;
            }

            private void Consume(char c)
            {
                if (!Peek(c)) throw Error("Expected '" + c + "'");
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0) throw Error("Invalid literal");
                _index += word.Length;
            }

            private FormatException Error(string message) => new FormatException(message + " at position " + _index + ".");
        }
    }
}
