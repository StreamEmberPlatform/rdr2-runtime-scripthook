//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.Linq;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// Read-only view of a JSON object: game settings, action arguments.
    /// Paths are dotted (<c>battle.maxHumans</c>); a flat key works too. A missing, null or mistyped value returns
    /// the given fallback; numbers must be finite.
    /// </summary>
    public sealed class LiveValues
    {
        private readonly Dictionary<string, object> _root;

        internal LiveValues(Dictionary<string, object> root)
        {
            _root = root ?? Json.NewObject();
        }

        public static LiveValues Empty => new LiveValues(null);

        /// <summary>Parses a JSON object (e.g. a file the script ships).</summary>
        /// <exception cref="FormatException">The text is not a JSON object.</exception>
        public static LiveValues Parse(string json) => new LiveValues(Json.ParseObject(json));

        internal Dictionary<string, object> Raw => _root;

        /// <summary>Top-level keys.</summary>
        public IReadOnlyList<string> Keys => _root.Keys.ToList();

        public bool Has(string path) => Find(path) != null;

        public string GetString(string path, string fallback = "")
        {
            object value = Find(path);
            return value is string || value is bool || value is double ? Json.ScalarText(value) : fallback;
        }

        public bool GetBool(string path, bool fallback = false)
        {
            object value = Find(path);
            if (value is bool flag) return flag;
            if (value is string text && bool.TryParse(text, out bool parsed)) return parsed;
            return fallback;
        }

        public double GetDouble(string path, double fallback = 0) =>
            Json.TryNumber(Find(path), out double value) ? value : fallback;

        /// <summary>Bounded number: fallback when missing, then clamped to [min, max].</summary>
        public double GetDouble(string path, double fallback, double min, double max) =>
            Math.Max(min, Math.Min(max, GetDouble(path, fallback)));

        public long GetLong(string path, long fallback = 0)
        {
            if (!Json.TryNumber(Find(path), out double value)) return fallback;
            if (value >= long.MaxValue) return long.MaxValue;
            if (value <= long.MinValue) return long.MinValue;
            return (long)Math.Round(value);
        }

        public int GetInt(string path, int fallback = 0)
        {
            long value = GetLong(path, fallback);
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));
        }

        /// <summary>Bounded integer: fallback when missing, then clamped to [min, max].</summary>
        public int GetInt(string path, int fallback, int min, int max) => Math.Max(min, Math.Min(max, GetInt(path, fallback)));

        /// <summary>List of texts; a single text is split on commas. Empty items are dropped.</summary>
        public IReadOnlyList<string> GetStringList(string path)
        {
            var result = new List<string>();
            object value = Find(path);
            if (value is List<object> list)
            {
                foreach (object item in list)
                {
                    string text = Json.ScalarText(item).Trim();
                    if (text.Length > 0) result.Add(text);
                }
            }
            else if (value is string single)
            {
                foreach (string part in single.Split(','))
                    if (part.Trim().Length > 0) result.Add(part.Trim());
            }
            return result;
        }

        /// <summary>Nested object (a copy); empty when missing.</summary>
        public LiveValues GetObject(string path) =>
            Find(path) is Dictionary<string, object> obj ? new LiveValues(Json.CloneObject(obj)) : Empty;

        /// <summary>Array of objects (copies); empty when missing.</summary>
        public IReadOnlyList<LiveValues> GetObjectList(string path)
        {
            var result = new List<LiveValues>();
            if (Find(path) is List<object> list)
                foreach (object item in list)
                    if (item is Dictionary<string, object> obj) result.Add(new LiveValues(Json.CloneObject(obj)));
            return result;
        }

        public string ToJson() => Json.Write(_root);

        public override string ToString() => ToJson();

        private object Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_root.TryGetValue(path, out object direct) && direct != null) return direct;
            object current = _root;
            foreach (string part in path.Split('.'))
            {
                if (!(current is Dictionary<string, object> obj) || !obj.TryGetValue(part, out current) || current == null) return null;
            }
            return current;
        }
    }
}
