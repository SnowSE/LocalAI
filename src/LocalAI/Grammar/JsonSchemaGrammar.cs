using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LocalAI.Grammar;

/// <summary>
/// Converts a JSON schema into a GBNF grammar, so a local model can only produce JSON that matches
/// it. Covers what structured output needs in practice: objects, arrays, strings, numbers,
/// booleans, null, <c>enum</c>, <c>const</c>, <c>anyOf</c>/<c>oneOf</c>, nullable type arrays and
/// local <c>$ref</c>s. Unknown keywords are ignored rather than rejected.
/// </summary>
public sealed class JsonSchemaGrammar
{
    private readonly Dictionary<string, string> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _refRules = new(StringComparer.Ordinal);
    private readonly JsonElement _root;

    private JsonSchemaGrammar(JsonElement root) => _root = root;

    /// <summary>A grammar whose <c>root</c> rule is a JSON value matching <paramref name="schema"/>.</summary>
    public static string FromSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return FromSchema(document.RootElement);
    }

    /// <inheritdoc cref="FromSchema(string)"/>
    public static string FromSchema(JsonElement schema)
    {
        var builder = new JsonSchemaGrammar(schema.Clone());
        var root = builder.Visit(schema, "root");
        if (root != "root")
            builder._rules["root"] = root;
        return builder.Render();
    }

    /// <summary>
    /// Add the rules for <paramref name="schema"/> to <paramref name="rules"/> under names starting
    /// with <paramref name="prefix"/>, and return the expression that matches it. Lets other
    /// grammars (tool calls) embed schemas.
    /// </summary>
    internal static string Embed(JsonElement schema, string prefix, IDictionary<string, string> rules)
    {
        var builder = new JsonSchemaGrammar(schema.Clone());
        var expression = builder.Visit(schema, prefix);
        foreach (var (name, body) in builder._rules)
            rules[name] = body;
        foreach (var (name, body) in Primitives)
            rules.TryAdd(name, body);
        return expression;
    }

    private static readonly (string Name, string Body)[] Primitives =
    [
        ("ws", "| \" \" | \"\\n\" [ \\t]{0,20}"),
        ("char", "[^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\bfnrt] | \"u\" [0-9a-fA-F]{4})"),
        ("string", "\"\\\"\" char* \"\\\"\" ws"),
        ("integral-part", "[0] | [1-9] [0-9]{0,15}"),
        ("decimal-part", "[0-9]{1,16}"),
        ("number", "(\"-\"? integral-part) (\".\" decimal-part)? ([eE] [-+]? integral-part)? ws"),
        ("integer", "(\"-\"? integral-part) ws"),
        ("boolean", "(\"true\" | \"false\") ws"),
        ("null", "\"null\" ws"),
        ("value", "object | array | string | number | boolean | null"),
        ("object", "\"{\" ws ( string \":\" ws value (\",\" ws string \":\" ws value)* )? \"}\" ws"),
        ("array", "\"[\" ws ( value (\",\" ws value)* )? \"]\" ws"),
    ];

    private string Render()
    {
        foreach (var (name, body) in Primitives)
            _rules.TryAdd(name, body);
        var text = new StringBuilder();
        text.Append("root ::= ").Append(_rules["root"]).Append('\n');
        foreach (var (name, body) in _rules.Where(r => r.Key != "root").OrderBy(r => r.Key, StringComparer.Ordinal))
            text.Append(name).Append(" ::= ").Append(body).Append('\n');
        return text.ToString();
    }

    /// <summary>Returns an expression for <paramref name="schema"/>, adding rules named after <paramref name="name"/> as needed.</summary>
    private string Visit(JsonElement schema, string name)
    {
        if (schema.ValueKind == JsonValueKind.True || schema.ValueKind != JsonValueKind.Object)
            return "value";

        if (schema.TryGetProperty("$ref", out var reference))
            return VisitRef(reference.GetString() ?? "");

        if (schema.TryGetProperty("const", out var constant))
            return Define(name, Literal(constant) + " ws");

        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
            return Define(name, "(" + string.Join(" | ", values.EnumerateArray().Select(Literal)) + ") ws");

        foreach (var keyword in (ReadOnlySpan<string>)["anyOf", "oneOf"])
        {
            if (schema.TryGetProperty(keyword, out var options) && options.ValueKind == JsonValueKind.Array)
            {
                var alternatives = options.EnumerateArray().Select((option, i) => Visit(option, $"{name}-{i}")).ToArray();
                return Define(name, string.Join(" | ", alternatives));
            }
        }

        if (schema.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array && all.GetArrayLength() == 1)
            return Visit(all[0], name);

        if (!schema.TryGetProperty("type", out var type))
            return schema.TryGetProperty("properties", out _) ? VisitObject(schema, name) : "value";

        if (type.ValueKind == JsonValueKind.Array)
        {
            var alternatives = type.EnumerateArray().Select((t, i) => VisitType(schema, t.GetString() ?? "", $"{name}-{i}")).ToArray();
            return Define(name, string.Join(" | ", alternatives));
        }
        return VisitType(schema, type.GetString() ?? "", name);
    }

    private string VisitType(JsonElement schema, string type, string name) => type switch
    {
        "object" => VisitObject(schema, name),
        "array" => VisitArray(schema, name),
        "string" => VisitString(schema, name),
        "integer" => "integer",
        "number" => "number",
        "boolean" => "boolean",
        "null" => "null",
        _ => "value",
    };

    private string VisitObject(JsonElement schema, string name)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return "object";

        var required = schema.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];

        // Properties come out in schema order. Required ones are always present; optional ones may
        // be left out. Each optional property carries its own leading comma, so the grammar can
        // express "any subset, in order" without a combinatorial explosion.
        var props = properties.EnumerateObject().Select(p =>
        {
            var valueRule = Visit(p.Value, $"{name}-{Sanitize(p.Name)}");
            return (p.Name, Pair: $"{Literal(p.Name)} \":\" ws {valueRule}", Required: required.Contains(p.Name));
        }).ToList();

        var body = new StringBuilder("\"{\" ws ");
        var firstRequired = props.FindIndex(p => p.Required);
        if (firstRequired < 0)
        {
            // Everything optional: allow any in-order subset, including none.
            var chain = OptionalChain(props.Select(p => p.Pair).ToList(), name);
            body.Append(chain is null ? "" : $"({chain})? ");
        }
        else
        {
            // Optional properties before the first required one form an in-order prefix.
            for (var i = 0; i < props.Count; i++)
            {
                var (_, pair, isRequired) = props[i];
                if (i < firstRequired)
                    body.Append($"({pair} \",\" ws)? ");
                else if (i == firstRequired)
                    body.Append(pair).Append(' ');
                else if (isRequired)
                    body.Append($"\",\" ws {pair} ");
                else
                    body.Append($"(\",\" ws {pair})? ");
            }
        }
        body.Append("\"}\" ws");
        return Define(name, body.ToString());
    }

    /// <summary>Any non-empty in-order subset of <paramref name="pairs"/>, comma-separated.</summary>
    private string? OptionalChain(List<string> pairs, string name)
    {
        if (pairs.Count == 0)
            return null;
        // rest-i ::= pair-i ("," rest-(i+1))? | rest-(i+1): either pair i comes first, or it is skipped.
        string? next = null;
        for (var i = pairs.Count - 1; i >= 0; i--)
        {
            var body = next is null ? pairs[i] : $"{pairs[i]} (\",\" ws {next})? | {next}";
            next = Define($"{name}-rest{i}", body);
        }
        return next;
    }

    private string VisitArray(JsonElement schema, string name)
    {
        var item = schema.TryGetProperty("items", out var items) ? Visit(items, $"{name}-item") : "value";
        var min = schema.TryGetProperty("minItems", out var mi) && mi.TryGetInt32(out var mn) ? mn : 0;
        int? max = schema.TryGetProperty("maxItems", out var ma) && ma.TryGetInt32(out var mx) ? mx : null;
        string list;
        if (max == 0)
            list = "";
        else if (min == 0)
            list = $"({item} (\",\" ws {item}){Repeat(0, max - 1)})?";
        else
            list = $"{item} (\",\" ws {item}){Repeat(min - 1, max - 1)}";
        return Define(name, $"\"[\" ws {list} \"]\" ws");
    }

    private string VisitString(JsonElement schema, string name)
    {
        if (schema.TryGetProperty("format", out var format))
        {
            var pattern = format.GetString() switch
            {
                "date" => "[0-9]{4} \"-\" [0-9]{2} \"-\" [0-9]{2}",
                "time" => "[0-9]{2} \":\" [0-9]{2} \":\" [0-9]{2} (\".\" [0-9]{1,6})? ([Zz] | [-+] [0-9]{2} \":\" [0-9]{2})?",
                "date-time" => "[0-9]{4} \"-\" [0-9]{2} \"-\" [0-9]{2} [Tt ] [0-9]{2} \":\" [0-9]{2} \":\" [0-9]{2} (\".\" [0-9]{1,6})? ([Zz] | [-+] [0-9]{2} \":\" [0-9]{2})?",
                "uuid" => "[0-9a-fA-F]{8} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{12}",
                _ => null,
            };
            if (pattern is not null)
                return Define(name, $"\"\\\"\" {pattern} \"\\\"\" ws");
        }
        if (schema.TryGetProperty("pattern", out var regex) && regex.GetString() is { Length: > 0 } p)
        {
            try
            {
                return Define(name, $"\"\\\"\" ({RegexGrammar.ToExpression(p, insideJsonString: true)}) \"\\\"\" ws");
            }
            catch (FormatException)
            {
                // Fall through to an unconstrained string for patterns we can't translate.
            }
        }
        var min = schema.TryGetProperty("minLength", out var mi) && mi.TryGetInt32(out var mn) ? mn : 0;
        int? max = schema.TryGetProperty("maxLength", out var ma) && ma.TryGetInt32(out var mx) ? mx : null;
        if (min == 0 && max is null)
            return "string";
        return Define(name, $"\"\\\"\" char{Repeat(min, max)} \"\\\"\" ws");
    }

    private string VisitRef(string reference)
    {
        if (_refRules.TryGetValue(reference, out var existing))
            return existing;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            return "value";

        var target = _root;
        foreach (var segment in reference[2..].Split('/'))
        {
            var key = segment.Replace("~1", "/").Replace("~0", "~");
            if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(key, out target))
                return "value";
        }
        var name = "ref-" + Sanitize(reference[2..]);
        _refRules[reference] = name;
        // Reserve the name first so recursive references terminate.
        _rules[name] = "value";
        var expression = Visit(target, name + "-body");
        _rules[name] = expression;
        return name;
    }

    private string Define(string name, string body)
    {
        name = Sanitize(name);
        _rules[name] = body;
        return name;
    }

    private static string Repeat(int min, int? max) => (min, max) switch
    {
        (0, null) => "*",
        (1, null) => "+",
        (0, 1) => "?",
        (_, null) => $"{{{min},}}",
        var (a, b) when a == b => $"{{{a}}}",
        _ => $"{{{min},{max}}}",
    };

    /// <summary>A GBNF literal matching exactly the JSON serialization of <paramref name="value"/>.</summary>
    internal static string Literal(JsonElement value) => Quote(value.GetRawText());

    /// <summary>A GBNF literal matching the JSON string <paramref name="text"/>, quotes included.</summary>
    internal static string Literal(string text) => Quote(JsonSerializer.Serialize(text));

    /// <summary>A GBNF double-quoted literal for exactly <paramref name="text"/>.</summary>
    internal static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }
        return builder.Append('"').ToString();
    }

    internal static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-');
        return builder.ToString();
    }
}
