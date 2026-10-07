using System.Globalization;
using System.Text;

namespace LocalAI.Grammar;

/// <summary>
/// Translates a regular expression into a GBNF grammar, so a local model's whole reply must match
/// it. Supports literals, <c>.</c>, character classes, <c>\d \w \s</c> (and their negations),
/// groups (including <c>(?:…)</c>), alternation and the quantifiers <c>* + ? {m} {m,} {m,n}</c>.
/// Anchors are accepted and ignored, because the grammar always matches the whole reply.
/// </summary>
public static class RegexGrammar
{
    /// <summary>A grammar whose <c>root</c> rule matches exactly the strings <paramref name="pattern"/> matches.</summary>
    /// <exception cref="FormatException">The pattern uses something this translator doesn't support, such as backreferences or lookaround.</exception>
    public static string FromPattern(string pattern) => $"root ::= {ToExpression(pattern)}\n";

    /// <summary>The GBNF expression for <paramref name="pattern"/>.</summary>
    /// <param name="insideJsonString">Keep matches valid inside a JSON string: no raw quotes, backslashes or control characters.</param>
    internal static string ToExpression(string pattern, bool insideJsonString = false)
    {
        var parser = new Parser(pattern, insideJsonString);
        var expression = parser.ParseAlternation();
        if (!parser.AtEnd)
            throw new FormatException($"Unexpected ')' at position {parser.Position} in /{pattern}/.");
        return expression.Length == 0 ? "\"\"" : expression;
    }

    private sealed class Parser(string pattern, bool insideJsonString)
    {
        private int _at;

        public bool AtEnd => _at >= pattern.Length;
        public int Position => _at;

        private char Peek => pattern[_at];

        public string ParseAlternation()
        {
            var branches = new List<string> { ParseSequence() };
            while (!AtEnd && Peek == '|')
            {
                _at++;
                branches.Add(ParseSequence());
            }
            return branches.Count == 1 ? branches[0] : string.Join(" | ", branches.Select(b => b.Length == 0 ? "\"\"" : b));
        }

        private string ParseSequence()
        {
            var items = new List<string>();
            var literal = new StringBuilder();

            void FlushLiteral()
            {
                if (literal.Length > 0)
                {
                    items.Add(JsonSchemaGrammar.Quote(literal.ToString()));
                    literal.Clear();
                }
            }

            while (!AtEnd && Peek is not ('|' or ')'))
            {
                var (atom, literalChar) = ParseAtom();
                if (atom is null)
                    continue; // An anchor.
                var quantifier = ParseQuantifier();
                if (literalChar is { } c && quantifier.Length == 0)
                {
                    literal.Append(c);
                    continue;
                }
                FlushLiteral();
                items.Add(atom + quantifier);
            }
            FlushLiteral();
            return string.Join(" ", items);
        }

        /// <summary>The next atom as a GBNF expression, and its character when it's a plain literal.</summary>
        private (string? Atom, char? Literal) ParseAtom()
        {
            var c = pattern[_at++];
            switch (c)
            {
                case '^' or '$':
                    return (null, null);
                case '.':
                    return (insideJsonString ? "[^\"\\\\\\x00-\\x1F]" : "[^\\n]", null);
                case '(':
                    if (_at < pattern.Length && Peek == '?')
                    {
                        if (_at + 1 < pattern.Length && pattern[_at + 1] == ':')
                            _at += 2;
                        else
                            throw new FormatException($"Lookaround and named groups aren't supported: /{pattern}/.");
                    }
                    var inner = ParseAlternation();
                    if (AtEnd || Peek != ')')
                        throw new FormatException($"Unclosed '(' in /{pattern}/.");
                    _at++;
                    return ($"({(inner.Length == 0 ? "\"\"" : inner)})", null);
                case '[':
                    return (ParseClass(), null);
                case '\\':
                    return ParseEscape(inClass: false);
                case '*' or '+' or '?' or '{':
                    throw new FormatException($"Quantifier '{c}' has nothing to repeat at position {_at - 1} in /{pattern}/.");
                default:
                    if (insideJsonString && c is '"' or '\\')
                        return (JsonSchemaGrammar.Quote("\\" + c), null);
                    return (JsonSchemaGrammar.Quote(c.ToString()), c);
            }
        }

        private (string Atom, char? Literal) ParseEscape(bool inClass)
        {
            if (AtEnd)
                throw new FormatException($"Pattern ends with a backslash: /{pattern}/.");
            var c = pattern[_at++];
            string? cls = c switch
            {
                'd' => "0-9",
                'w' => "a-zA-Z0-9_",
                's' => " \\t\\n\\r",
                _ => null,
            };
            if (cls is not null)
                return (inClass ? cls : $"[{cls}]", null);
            string? negated = c switch
            {
                'D' => "^0-9",
                'W' => "^a-zA-Z0-9_",
                'S' => "^ \\t\\n\\r",
                _ => null,
            };
            if (negated is not null)
            {
                if (inClass)
                    throw new FormatException($"\\{c} inside a character class isn't supported: /{pattern}/.");
                return ($"[{negated}]", null);
            }
            if (char.IsAsciiDigit(c) && c != '0')
                throw new FormatException($"Backreferences aren't supported: /{pattern}/.");
            var literal = c switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => c,
            };
            return inClass ? (ClassChar(literal), null) : (JsonSchemaGrammar.Quote(literal.ToString()), literal);
        }

        private string ParseClass()
        {
            var builder = new StringBuilder("[");
            if (!AtEnd && Peek == '^')
            {
                builder.Append('^');
                _at++;
            }
            var first = true;
            while (!AtEnd && (Peek != ']' || first))
            {
                first = false;
                var c = pattern[_at++];
                if (c == '\\')
                    builder.Append(ParseEscape(inClass: true).Atom);
                else if (c == '-' )
                    builder.Append('-');
                else
                    builder.Append(ClassChar(c));
            }
            if (AtEnd)
                throw new FormatException($"Unclosed '[' in /{pattern}/.");
            _at++;
            return builder.Append(']').ToString();
        }

        private static string ClassChar(char c) => c switch
        {
            '\\' => "\\\\",
            ']' => "\\]",
            '[' => "\\[",
            '^' => "\\^",
            '-' => "\\-",
            '\n' => "\\n",
            '\t' => "\\t",
            '\r' => "\\r",
            < ' ' => "\\x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture),
            _ => c.ToString(),
        };

        private string ParseQuantifier()
        {
            if (AtEnd)
                return "";
            var c = Peek;
            string quantifier;
            if (c is '*' or '+' or '?')
            {
                _at++;
                quantifier = c.ToString();
            }
            else if (c == '{' && TryParseBraces(out var braces))
            {
                quantifier = braces;
            }
            else
            {
                return "";
            }
            // Lazy and possessive suffixes don't change what matches, only how.
            if (!AtEnd && Peek is '?' or '+')
                _at++;
            return quantifier;
        }

        private bool TryParseBraces(out string quantifier)
        {
            quantifier = "";
            var close = pattern.IndexOf('}', _at);
            if (close < 0)
                return false;
            var body = pattern[(_at + 1)..close];
            var parts = body.Split(',');
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var min))
                return false;
            if (parts.Length == 2 && parts[1].Length > 0 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;
            _at = close + 1;
            quantifier = "{" + body + "}";
            return true;
        }
    }
}
