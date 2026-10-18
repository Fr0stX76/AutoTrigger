using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoTrigger.Core.Json
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
        public static readonly JsonValue True = new JsonValue(JsonKind.Bool) { _bool = true };
        public static readonly JsonValue False = new JsonValue(JsonKind.Bool) { _bool = false };

        private bool _bool;
        private string _string;
        private double _number;
        private string _numberText;
        private List<JsonValue> _items;
        private List<KeyValuePair<string, JsonValue>> _properties;
        private Dictionary<string, int> _index;

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
        }

        public JsonKind Kind { get; }

        public static JsonValue FromString(string value)
        {
            if (value == null)
                return Null;
            return new JsonValue(JsonKind.String) { _string = value };
        }

        public static JsonValue FromBool(bool value) => value ? True : False;

        public static JsonValue FromNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return Null;
            return new JsonValue(JsonKind.Number) { _number = value };
        }

        public static JsonValue FromNumber(long value)
        {
            return new JsonValue(JsonKind.Number)
            {
                _number = value,
                _numberText = value.ToString(CultureInfo.InvariantCulture),
            };
        }

        internal static JsonValue FromNumberText(string text, double value)
        {
            return new JsonValue(JsonKind.Number) { _number = value, _numberText = text };
        }

        public static JsonValue NewArray() => new JsonValue(JsonKind.Array) { _items = new List<JsonValue>() };

        public static JsonValue NewObject() => new JsonValue(JsonKind.Object)
        {
            _properties = new List<KeyValuePair<string, JsonValue>>(),
            _index = new Dictionary<string, int>(StringComparer.Ordinal),
        };

        public static JsonValue Parse(string text) => JsonParser.Parse(text);

        public string AsString() => Kind == JsonKind.String ? _string : null;

        public bool? AsBool() => Kind == JsonKind.Bool ? _bool : (bool?)null;

        public double? AsNumber() => Kind == JsonKind.Number ? _number : (double?)null;

        public JsonValue this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object || key == null)
                    return null;
                return _index.TryGetValue(key, out int i) ? _properties[i].Value : null;
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Properties
        {
            get
            {
                if (Kind != JsonKind.Object)
                    yield break;
                foreach (KeyValuePair<string, JsonValue> property in _properties)
                    yield return property;
            }
        }

        public JsonValue Set(string key, JsonValue value)
        {
            if (Kind != JsonKind.Object)
                throw new InvalidOperationException("Not a JSON object.");
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            value = value ?? Null;
            if (_index.TryGetValue(key, out int i))
                _properties[i] = new KeyValuePair<string, JsonValue>(key, value);
            else
            {
                _index[key] = _properties.Count;
                _properties.Add(new KeyValuePair<string, JsonValue>(key, value));
            }
            return this;
        }

        public JsonValue Set(string key, string value) => Set(key, FromString(value));

        public JsonValue Set(string key, bool value) => Set(key, FromBool(value));

        public JsonValue Set(string key, long value) => Set(key, FromNumber(value));

        public JsonValue Set(string key, double value) => Set(key, FromNumber(value));

        public int Count => Kind switch
        {
            JsonKind.Array => _items.Count,
            JsonKind.Object => _properties.Count,
            _ => 0,
        };

        public JsonValue this[int index] => Kind == JsonKind.Array ? _items[index] : null;

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (Kind != JsonKind.Array)
                    yield break;
                foreach (JsonValue item in _items)
                    yield return item;
            }
        }

        public JsonValue Add(JsonValue value)
        {
            if (Kind != JsonKind.Array)
                throw new InvalidOperationException("Not a JSON array.");
            _items.Add(value ?? Null);
            return this;
        }

        public string ToJson(bool indented = true)
        {
            var sb = new StringBuilder();
            Write(sb, indented, 0);
            return sb.ToString();
        }

        public override string ToString() => ToJson(false);

        private void Write(StringBuilder sb, bool indented, int depth)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(_bool ? "true" : "false");
                    break;
                case JsonKind.Number:
                    sb.Append(_numberText ?? FormatNumber(_number));
                    break;
                case JsonKind.String:
                    WriteString(sb, _string);
                    break;
                case JsonKind.Array:
                    if (_items.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(',');
                        NewLine(sb, indented, depth + 1);
                        _items[i].Write(sb, indented, depth + 1);
                    }
                    NewLine(sb, indented, depth);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (_properties.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append('{');
                    for (int i = 0; i < _properties.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(',');
                        NewLine(sb, indented, depth + 1);
                        WriteString(sb, _properties[i].Key);
                        sb.Append(indented ? ": " : ":");
                        _properties[i].Value.Write(sb, indented, depth + 1);
                    }
                    NewLine(sb, indented, depth);
                    sb.Append('}');
                    break;
            }
        }

        private static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (!indented)
                return;
            sb.Append("\r\n");
            sb.Append(' ', depth * 2);
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value) < 9007199254740992.0 && Math.Floor(value) == value)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message, int line, int column)
            : base($"{message} (line {line}, column {column})")
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }

        public int Column { get; }
    }
}
