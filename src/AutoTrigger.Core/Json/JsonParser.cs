using System;
using System.Globalization;
using System.Text;

namespace AutoTrigger.Core.Json
{
    internal sealed class JsonParser
    {
        private const int MaxDepth = 256;

        private readonly string _text;
        private int _pos;

        private JsonParser(string text)
        {
            _text = text;
        }

        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            var parser = new JsonParser(text);
            if (parser._pos < text.Length && text[parser._pos] == (char)0xFEFF)
                parser._pos++;
            parser.SkipWhitespace();
            JsonValue value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (parser._pos != text.Length)
                throw parser.Error("Unexpected text after the JSON value");
            return value;
        }

        private JsonValue ParseValue(int depth)
        {
            if (depth > MaxDepth)
                throw Error("JSON nesting is too deep");
            if (_pos >= _text.Length)
                throw Error("Unexpected end of JSON");

            char c = _text[_pos];
            switch (c)
            {
                case '{':
                    return ParseObject(depth);
                case '[':
                    return ParseArray(depth);
                case '"':
                    return JsonValue.FromString(ParseString());
                case 't':
                    ExpectWord("true");
                    return JsonValue.True;
                case 'f':
                    ExpectWord("false");
                    return JsonValue.False;
                case 'n':
                    ExpectWord("null");
                    return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber();
                    throw Error($"Unexpected character '{c}'");
            }
        }

        private JsonValue ParseObject(int depth)
        {
            var obj = JsonValue.NewObject();
            _pos++;
            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                return obj;
            }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw Error("Expected a property name");
                string key = ParseString();
                SkipWhitespace();
                if (Peek() != ':')
                    throw Error("Expected ':'");
                _pos++;
                SkipWhitespace();
                obj.Set(key, ParseValue(depth + 1));
                SkipWhitespace();
                char c = Peek();
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == '}')
                {
                    _pos++;
                    return obj;
                }
                throw Error("Expected ',' or '}'");
            }
        }

        private JsonValue ParseArray(int depth)
        {
            var arr = JsonValue.NewArray();
            _pos++;
            SkipWhitespace();
            if (Peek() == ']')
            {
                _pos++;
                return arr;
            }
            while (true)
            {
                SkipWhitespace();
                arr.Add(ParseValue(depth + 1));
                SkipWhitespace();
                char c = Peek();
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == ']')
                {
                    _pos++;
                    return arr;
                }
                throw Error("Expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length)
                    throw Error("Unterminated string");
                char c = _text[_pos++];
                if (c == '"')
                    return sb.ToString();
                if (c < 0x20)
                    throw Error("Control character in string");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (_pos >= _text.Length)
                    throw Error("Unterminated escape");
                char e = _text[_pos++];
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
                        if (_pos + 4 > _text.Length)
                            throw Error("Invalid \\u escape");
                        if (!int.TryParse(_text.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            throw Error("Invalid \\u escape");
                        _pos += 4;
                        sb.Append((char)code);
                        break;
                    default:
                        throw Error($"Invalid escape '\\{e}'");
                }
            }
        }

        private JsonValue ParseNumber()
        {
            int start = _pos;
            if (Peek() == '-')
                _pos++;
            if (Peek() == '0')
                _pos++;
            else if (IsDigit(Peek()))
            {
                while (IsDigit(Peek()))
                    _pos++;
            }
            else
                throw Error("Invalid number");

            if (Peek() == '.')
            {
                _pos++;
                if (!IsDigit(Peek()))
                    throw Error("Invalid number");
                while (IsDigit(Peek()))
                    _pos++;
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                _pos++;
                if (Peek() == '+' || Peek() == '-')
                    _pos++;
                if (!IsDigit(Peek()))
                    throw Error("Invalid number");
                while (IsDigit(Peek()))
                    _pos++;
            }
            string text = _text.Substring(start, _pos - start);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsInfinity(value) || double.IsNaN(value))
                throw Error("Number out of range");
            return JsonValue.FromNumberText(text, value);
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                throw Error("Invalid literal");
            _pos += word.Length;
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    _pos++;
                else
                    break;
            }
        }

        private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private JsonException Error(string message)
        {
            int line = 1, column = 1;
            for (int i = 0; i < _pos && i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                    column++;
            }
            return new JsonException(message, line, column);
        }
    }
}
