using System.Text.Json;
using LocalAI.Grammar;

namespace LocalAI.Tests;

public class JsonSchemaGrammarTests
{
    [Fact]
    public void RequiredPropertiesAppearInOrder()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""
            {"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}},"required":["name","age"]}
            """);

        var root = Rule(grammar, "root");
        Assert.Contains("\"\\\"name\\\"\" \":\" ws string", root, StringComparison.Ordinal);
        Assert.Contains("\",\" ws \"\\\"age\\\"\" \":\" ws integer", root, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalPropertiesCanBeLeftOut()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""
            {"type":"object","properties":{"name":{"type":"string"},"nickname":{"type":"string"}},"required":["name"]}
            """);

        Assert.Contains("(\",\" ws \"\\\"nickname\\\"\" \":\" ws string)?", Rule(grammar, "root"), StringComparison.Ordinal);
    }

    [Fact]
    public void AllOptionalPropertiesAllowAnyInOrderSubset()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""
            {"type":"object","properties":{"a":{"type":"string"},"b":{"type":"string"}}}
            """);

        Assert.Contains("root-rest0", Rule(grammar, "root"), StringComparison.Ordinal);
        Assert.Contains("| root-rest1", Rule(grammar, "root-rest0"), StringComparison.Ordinal);
    }

    [Fact]
    public void EnumsBecomeLiteralAlternatives()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""{"enum":["red","green",3]}""");

        Assert.Equal("(\"\\\"red\\\"\" | \"\\\"green\\\"\" | \"3\") ws", Rule(grammar, "root"));
    }

    [Fact]
    public void ArraysRespectItemCounts()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""{"type":"array","items":{"type":"number"},"minItems":1,"maxItems":3}""");

        Assert.Equal("\"[\" ws number (\",\" ws number){0,2} \"]\" ws", Rule(grammar, "root"));
    }

    [Fact]
    public void NullableTypesAllowNull()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""{"type":["string","null"]}""");

        Assert.Equal("string | null", Rule(grammar, "root"));
    }

    [Fact]
    public void LocalReferencesResolve()
    {
        var grammar = JsonSchemaGrammar.FromSchema("""
            {"type":"object","properties":{"home":{"$ref":"#/$defs/address"}},"required":["home"],
             "$defs":{"address":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}}
            """);

        Assert.Contains("ref--defs-address", Rule(grammar, "root"), StringComparison.Ordinal);
        Assert.Contains("\\\"city\\\"", grammar, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaFromMicrosoftExtensionsAiConverts()
    {
        // The shape AIJsonUtilities produces for a record with an enum and a list.
        var schema = Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(typeof(Contact));
        var grammar = JsonSchemaGrammar.FromSchema(schema);

        Assert.Contains("\\\"name\\\"", grammar, StringComparison.Ordinal);
        Assert.Contains("\\\"topics\\\"", grammar, StringComparison.Ordinal);
        Assert.Contains("\\\"High\\\"", grammar, StringComparison.Ordinal);
    }

    public enum Urgency { Low, High }

    public sealed record Contact(string Name, string? Email, List<string> Topics, Urgency Urgency);

    internal static string Rule(string grammar, string name)
    {
        var prefix = name + " ::= ";
        var line = grammar.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"No rule {name} in:\n{grammar}");
        var body = line[prefix.Length..];
        // Follow a root that only names another rule.
        return grammar.Split('\n').Any(l => l.StartsWith(body + " ::= ", StringComparison.Ordinal)) ? Rule(grammar, body) : body;
    }
}

public class RegexGrammarTests
{
    [Fact]
    public void AlternationOfWords() =>
        Assert.Equal("root ::= (\"positive\" | \"negative\" | \"mixed\")\n", RegexGrammar.FromPattern("(positive|negative|mixed)"));

    [Fact]
    public void ClassesEscapesAndQuantifiers() =>
        Assert.Equal("root ::= [0-9]{3} \"-\" [A-Za-z_]+ [ \\t\\n\\r]?\n", RegexGrammar.FromPattern("^\\d{3}-[A-Za-z_]+\\s?$"));

    [Fact]
    public void LiteralRunsMerge() =>
        Assert.Equal("root ::= \"ab\" \"c\"*\n", RegexGrammar.FromPattern("abc*"));

    [Fact]
    public void NonCapturingGroupsAndDot() =>
        Assert.Equal("root ::= (\"a\" | \"b\") [^\\n]\n", RegexGrammar.FromPattern("(?:a|b)."));

    [Theory]
    [InlineData("(a)\\1")]
    [InlineData("(?=a)")]
    [InlineData("[abc")]
    [InlineData("*a")]
    public void UnsupportedPatternsThrow(string pattern) =>
        Assert.Throws<FormatException>(() => RegexGrammar.FromPattern(pattern));
}

public class ToolCallGrammarTests
{
    [Fact]
    public void EachToolIsAnAlternative()
    {
        using var weather = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""");
        using var empty = JsonDocument.Parse("{}");
        var grammar = ToolCallGrammar.ForCallBody(
            [new ToolSignature("get_weather", weather.RootElement), new ToolSignature("current_time", empty.RootElement)],
            "</tool_call>");

        Assert.StartsWith("root ::= ws (tool0 | tool1) ws \"</tool_call>\"", grammar, StringComparison.Ordinal);
        Assert.Contains("\"\\\"get_weather\\\"\"", grammar, StringComparison.Ordinal);
        Assert.Contains("tool1-args ::= \"{\" ws \"}\" ws", grammar, StringComparison.Ordinal);
    }
}
