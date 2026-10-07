using System.Text;
using System.Text.Json;

namespace LocalAI.Grammar;

/// <summary>A tool the model may call: its name and the JSON schema of its arguments.</summary>
public sealed record ToolSignature(string Name, JsonElement ParametersSchema);

/// <summary>
/// The grammar for the body of one tool call, once the model has opened it. Each tool contributes
/// one alternative, so the model can only name a real tool and only pass arguments its schema allows.
/// </summary>
public static class ToolCallGrammar
{
    /// <summary>
    /// A grammar for <c>{"name": "…", "arguments": {…}}</c> followed by <paramref name="closing"/>,
    /// with one alternative per tool.
    /// </summary>
    public static string ForCallBody(IReadOnlyList<ToolSignature> tools, string closing)
    {
        if (tools.Count == 0)
            throw new ArgumentException("At least one tool is needed.", nameof(tools));

        var rules = new Dictionary<string, string>(StringComparer.Ordinal);
        var alternatives = new List<string>();
        foreach (var (tool, index) in tools.Select((t, i) => (t, i)))
        {
            var arguments = JsonSchemaGrammar.Embed(Normalize(tool.ParametersSchema), $"tool{index}-args", rules);
            var call = $"tool{index}";
            rules[call] = $"\"{{\" ws \"\\\"name\\\"\" ws \":\" ws {JsonSchemaGrammar.Literal(tool.Name)} ws \",\" ws \"\\\"arguments\\\"\" ws \":\" ws {arguments} \"}}\"";
            alternatives.Add(call);
        }

        var text = new StringBuilder();
        text.Append("root ::= ws (").Append(string.Join(" | ", alternatives)).Append(") ws ")
            .Append(JsonSchemaGrammar.Quote(closing)).Append('\n');
        foreach (var (name, body) in rules.OrderBy(r => r.Key, StringComparer.Ordinal))
            text.Append(name).Append(" ::= ").Append(body).Append('\n');
        return text.ToString();
    }

    /// <summary>A tool with no parameters, or an untyped schema, still needs an object for its arguments.</summary>
    private static JsonElement Normalize(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && (schema.TryGetProperty("properties", out _) || schema.TryGetProperty("type", out _)))
            return schema;
        using var empty = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return empty.RootElement.Clone();
    }
}
