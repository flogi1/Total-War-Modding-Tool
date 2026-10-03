using System.Globalization;
using System.Text;

namespace TotalWarModTool.Schema;

public abstract record RonValue;
public sealed record RonString(string Value) : RonValue;
public sealed record RonNumber(string Raw) : RonValue;
public sealed record RonBool(bool Value) : RonValue;

/// <summary>A bare name such as <c>None</c> or <c>StringU8</c>, optionally followed by a bracketed payload such as <c>Some(("t", "c"))</c>.</summary>
public sealed record RonIdent(string Name, RonValue? Payload) : RonValue;

/// <summary><c>(name: value, …)</c></summary>
public sealed record RonStruct(IReadOnlyDictionary<string, RonValue> Fields) : RonValue;

/// <summary><c>(value, …)</c></summary>
public sealed record RonTuple(IReadOnlyList<RonValue> Items) : RonValue;

/// <summary><c>{ key: value, … }</c></summary>
public sealed record RonMap(IReadOnlyList<KeyValuePair<RonValue, RonValue>> Entries) : RonValue;

/// <summary><c>[value, …]</c></summary>
public sealed record RonList(IReadOnlyList<RonValue> Items) : RonValue;

/// <summary>Where and why parsing stopped. Line and column are 1-based.</summary>
public sealed record RonError(int Line, int Column, string Message);

/// <summary>
/// Parser for the subset of RON that the RPFM schema files use. It never throws for bad input: a failure is reported as
/// <see cref="RonError"/>. Nesting deeper than <see cref="MaxDepth"/> is an error, so hostile input cannot overflow the stack.
/// </summary>
public static class RonParser
{
    public const int MaxDepth = 200;

    public static bool TryParse(string text, out RonValue? value, out RonError? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new Parser(text);
        try
        {
            parser.SkipTrivia();
            var root = parser.ParseValue(0);
            parser.SkipTrivia();
            if (!parser.AtEnd)
            {
                throw parser.Fail("Unexpected text after the root value");
            }

            value = root;
            error = null;
            return true;
        }
        catch (RonSyntaxException ex)
        {
            value = null;
            error = parser.ToError(ex.Offset, ex.Message);
            return false;
        }
    }

    private sealed class RonSyntaxException(int offset, string message) : Exception(message)
    {
        public int Offset { get; } = offset;
    }

    private sealed class Parser(string text)
    {
        private static readonly RonBool True = new(true);
        private static readonly RonBool False = new(false);

        private int _pos;

        // Equal names occur hundreds of thousands of times in a real schema; sharing them keeps the tree small.
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RonIdent> _plainIdents = new(StringComparer.Ordinal);

        public bool AtEnd => _pos >= text.Length;

        public RonSyntaxException Fail(string message) => new(_pos, message);

        public RonError ToError(int offset, string message)
        {
            var line = 1;
            var lineStart = 0;
            for (var i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return new RonError(line, offset - lineStart + 1, message);
        }

        public void SkipTrivia()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (char.IsWhiteSpace(c))
                {
                    _pos++;
                }
                else if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        public RonValue ParseValue(int depth)
        {
            if (AtEnd)
            {
                throw Fail("Unexpected end of input, a value is missing");
            }

            var c = text[_pos];
            switch (c)
            {
                case '"': return ParseString();
                case '(': return ParseParen(depth);
                case '[': return ParseList(depth);
                case '{': return ParseMap(depth);
            }

            if (char.IsAsciiDigit(c) || c is '-' or '+' or '.')
            {
                return ParseNumber();
            }

            if (IsIdentStart(c))
            {
                return ParseIdentOrBool(depth);
            }

            throw Fail("Unexpected character '" + c + "'");
        }

        /// <summary><paramref name="depth"/> is the number of containers around the value that is about to open one.</summary>
        private void EnterContainer(int depth)
        {
            if (depth >= MaxDepth)
            {
                throw Fail("Nesting is deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_';
        private static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        private string ReadWord()
        {
            var start = _pos;
            while (_pos < text.Length && IsIdentPart(text[_pos]))
            {
                _pos++;
            }

            var word = text.Substring(start, _pos - start);
            if (_names.TryGetValue(word, out var shared))
            {
                return shared;
            }

            _names[word] = word;
            return word;
        }

        private RonValue ParseIdentOrBool(int depth)
        {
            var name = ReadWord();
            if (name == "true")
            {
                return True;
            }

            if (name == "false")
            {
                return False;
            }

            SkipTrivia();
            if (_pos < text.Length && text[_pos] == '(')
            {
                return new RonIdent(name, ParseParen(depth));
            }

            if (!_plainIdents.TryGetValue(name, out var plain))
            {
                plain = new RonIdent(name, null);
                _plainIdents[name] = plain;
            }

            return plain;
        }

        private RonNumber ParseNumber()
        {
            var start = _pos;
            var digits = 0;
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (char.IsAsciiDigit(c))
                {
                    digits++;
                }
                else if (c is not ('-' or '+' or '.' or 'e' or 'E' or '_'))
                {
                    break;
                }

                _pos++;
            }

            if (digits == 0)
            {
                _pos = start;
                throw Fail("Malformed number");
            }

            return new RonNumber(text.Substring(start, _pos - start));
        }

        private RonString ParseString()
        {
            _pos++; // opening quote
            var start = _pos;
            StringBuilder? builder = null;
            while (true)
            {
                if (_pos >= text.Length)
                {
                    throw Fail("Unterminated string");
                }

                var c = text[_pos];
                if (c == '"')
                {
                    var value = builder is null
                        ? text.Substring(start, _pos - start)
                        : builder.Append(text, start, _pos - start).ToString();
                    _pos++;
                    return new RonString(value);
                }

                if (c != '\\')
                {
                    _pos++;
                    continue;
                }

                builder ??= new StringBuilder();
                builder.Append(text, start, _pos - start);
                AppendEscape(builder);
                start = _pos;
            }
        }

        private void AppendEscape(StringBuilder builder)
        {
            var escapeStart = _pos;
            _pos++; // backslash
            if (_pos >= text.Length)
            {
                throw Fail("Unterminated string");
            }

            var c = text[_pos++];
            switch (c)
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case '0': builder.Append('\0'); break;
                case '\'': builder.Append('\''); break;
                case 'u': AppendUnicodeEscape(builder, escapeStart); break;
                default: throw new RonSyntaxException(escapeStart, "Unknown escape '\\" + c + "'");
            }
        }

        private void AppendUnicodeEscape(StringBuilder builder, int escapeStart)
        {
            if (_pos >= text.Length || text[_pos] != '{')
            {
                throw new RonSyntaxException(escapeStart, "Malformed \\u escape, expected '{'");
            }

            _pos++;
            var codePoint = 0;
            var digits = 0;
            while (_pos < text.Length && text[_pos] != '}')
            {
                var c = text[_pos];
                if (!char.IsAsciiHexDigit(c) || ++digits > 6)
                {
                    throw new RonSyntaxException(escapeStart, "Malformed \\u escape");
                }

                codePoint = codePoint * 16 + (c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
                _pos++;
            }

            if (_pos >= text.Length)
            {
                throw Fail("Unterminated string");
            }

            _pos++; // closing brace
            if (digits == 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                throw new RonSyntaxException(escapeStart, "\\u escape is not a valid character");
            }

            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        /// <summary>The first content decides: <c>name:</c> makes a struct, anything else a tuple, empty brackets an empty struct.</summary>
        private RonValue ParseParen(int depth)
        {
            EnterContainer(depth);
            _pos++; // '('
            SkipTrivia();
            if (_pos < text.Length && text[_pos] == ')')
            {
                _pos++;
                return new RonStruct(new Dictionary<string, RonValue>());
            }

            return StartsWithFieldName() ? ParseStructBody(depth + 1) : ParseTupleBody(depth + 1);
        }

        private bool StartsWithFieldName()
        {
            if (_pos >= text.Length || !IsIdentStart(text[_pos]))
            {
                return false;
            }

            var saved = _pos;
            while (_pos < text.Length && IsIdentPart(text[_pos]))
            {
                _pos++;
            }

            SkipTrivia();
            var isField = _pos < text.Length && text[_pos] == ':';
            _pos = saved;
            return isField;
        }

        private RonStruct ParseStructBody(int depth)
        {
            var fields = new Dictionary<string, RonValue>();
            while (true)
            {
                SkipTrivia();
                if (_pos < text.Length && text[_pos] == ')')
                {
                    _pos++;
                    return new RonStruct(fields);
                }

                if (_pos >= text.Length || !IsIdentStart(text[_pos]))
                {
                    throw Fail(AtEnd ? "Unexpected end of input, ')' is missing" : "Expected a field name");
                }

                var name = ReadWord();
                SkipTrivia();
                Expect(':');
                SkipTrivia();
                fields[name] = ParseValue(depth);
                SkipTrivia();
                if (!ConsumeSeparator(')'))
                {
                    throw Fail(AtEnd ? "Unexpected end of input, ')' is missing" : "Expected ',' or ')'");
                }
            }
        }

        private RonTuple ParseTupleBody(int depth)
        {
            var items = new List<RonValue>();
            while (true)
            {
                SkipTrivia();
                if (_pos < text.Length && text[_pos] == ')')
                {
                    _pos++;
                    return new RonTuple(items);
                }

                items.Add(ParseValue(depth));
                SkipTrivia();
                if (!ConsumeSeparator(')'))
                {
                    throw Fail(AtEnd ? "Unexpected end of input, ')' is missing" : "Expected ',' or ')'");
                }
            }
        }

        private RonList ParseList(int depth)
        {
            EnterContainer(depth);
            _pos++; // '['
            var items = new List<RonValue>();
            while (true)
            {
                SkipTrivia();
                if (_pos < text.Length && text[_pos] == ']')
                {
                    _pos++;
                    return new RonList(items);
                }

                items.Add(ParseValue(depth + 1));
                SkipTrivia();
                if (!ConsumeSeparator(']'))
                {
                    throw Fail(AtEnd ? "Unexpected end of input, ']' is missing" : "Expected ',' or ']'");
                }
            }
        }

        private RonMap ParseMap(int depth)
        {
            EnterContainer(depth);
            _pos++; // '{'
            var entries = new List<KeyValuePair<RonValue, RonValue>>();
            while (true)
            {
                SkipTrivia();
                if (_pos < text.Length && text[_pos] == '}')
                {
                    _pos++;
                    return new RonMap(entries);
                }

                var key = ParseValue(depth + 1);
                SkipTrivia();
                Expect(':');
                SkipTrivia();
                var entryValue = ParseValue(depth + 1);
                entries.Add(new KeyValuePair<RonValue, RonValue>(key, entryValue));
                SkipTrivia();
                if (!ConsumeSeparator('}'))
                {
                    throw Fail(AtEnd ? "Unexpected end of input, '}' is missing" : "Expected ',' or '}'");
                }
            }
        }

        private void Expect(char expected)
        {
            if (_pos >= text.Length)
            {
                throw Fail("Unexpected end of input, '" + expected + "' is missing");
            }

            if (text[_pos] != expected)
            {
                throw Fail("Expected '" + expected + "'");
            }

            _pos++;
        }

        /// <summary>After an item: a comma is consumed (the caller then sees the next item or the closing bracket); the closing bracket itself is left for the caller.</summary>
        private bool ConsumeSeparator(char close)
        {
            if (_pos < text.Length && text[_pos] == ',')
            {
                _pos++;
                return true;
            }

            return _pos < text.Length && text[_pos] == close;
        }
    }
}
