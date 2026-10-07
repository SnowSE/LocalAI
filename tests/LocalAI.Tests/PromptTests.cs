using System.Text.Json.Nodes;
using LocalAI.Llama;
using Microsoft.Extensions.AI;

namespace LocalAI.Tests;

public class ChatTemplateTests
{
    private static ChatTemplate Load(string name, string bos = "", string eos = "") =>
        new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", name)), bos, eos);

    private static JsonArray Messages(params (string Role, string Content)[] messages) =>
        [.. messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content })];

    [Fact]
    public void QwenRendersAConversation()
    {
        var prompt = Load("Qwen_Qwen3-0.6B.jinja").Render(Messages(("system", "Be brief."), ("user", "Hi")), null, enableThinking: true);

        Assert.Equal("<|im_start|>system\nBe brief.<|im_end|>\n<|im_start|>user\nHi<|im_end|>\n<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void QwenWithoutThinkingGetsAnEmptyThinkBlock()
    {
        var prompt = Load("Qwen_Qwen3-0.6B.jinja").Render(Messages(("user", "Hi")), null, enableThinking: false);

        Assert.EndsWith("<|im_start|>assistant\n<think>\n\n</think>\n\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void QwenRendersToolsCallsAndResults()
    {
        var template = Load("Qwen_Qwen3-0.6B.jinja");
        Assert.True(template.SupportsTools);
        Assert.True(template.SupportsThinking);

        var prompt = PromptBuilder.Build(
            [
                new ChatMessage(ChatRole.User, "Weather in Oslo?"),
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather", new Dictionary<string, object?> { ["city"] = "Oslo" })]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "{\"temp\": 3}")]),
            ],
            new ChatOptions { Tools = [AIFunctionFactory.Create((string city) => "", "get_weather", "Get the weather")] },
            mediaMarker: null);
        var text = template.Render(prompt.Messages, prompt.Tools, enableThinking: false);

        Assert.Contains("\"name\": \"get_weather\"", text, StringComparison.Ordinal);
        Assert.Contains("<tool_call>\n{\"name\": \"get_weather\", \"arguments\": {\"city\":\"Oslo\"}}\n</tool_call>", text.Replace("{\"city\": \"Oslo\"}", "{\"city\":\"Oslo\"}"), StringComparison.Ordinal);
        Assert.Contains("<tool_response>\n{\"temp\": 3}\n</tool_response>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesWithoutToolSupportGetInstructionsInTheSystemPrompt()
    {
        var template = new ChatTemplate(null, "", "");
        Assert.False(template.SupportsTools);
        var tools = new JsonArray { new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "f" } } };

        var prompt = template.Render(Messages(("user", "Hi")), tools, enableThinking: false);

        Assert.StartsWith("<|im_start|>system\nYou may call one or more functions", prompt, StringComparison.Ordinal);
        Assert.Contains("<tool_call>{\"name\": <function name>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void LlamaRendersWithBosToken()
    {
        var prompt = Load("unsloth_Llama-3.2-1B-Instruct.jinja", bos: "<|begin_of_text|>").Render(Messages(("user", "Hi")), null, false);

        Assert.StartsWith("<|begin_of_text|><|start_header_id|>system<|end_header_id|>", prompt, StringComparison.Ordinal);
        Assert.EndsWith("<|start_header_id|>assistant<|end_header_id|>\n\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void GemmaRendersItsTurns()
    {
        var prompt = Load("unsloth_gemma-3-1b-it.jinja", bos: "<bos>").Render(Messages(("system", "Be brief."), ("user", "Hi")), null, false);

        Assert.Equal("<bos><start_of_turn>user\nBe brief.\n\nHi<end_of_turn>\n<start_of_turn>model\n", prompt);
    }

    [Fact]
    public void InstructionsJoinTheSystemPrompt()
    {
        var prompt = PromptBuilder.Build([new ChatMessage(ChatRole.System, "A"), new ChatMessage(ChatRole.User, "Hi")], new ChatOptions { Instructions = "B" }, null);

        Assert.Equal("A\n\nB", prompt.Messages[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ImagesNeedAVisionModel() =>
        Assert.Throws<NotSupportedException>(() => PromptBuilder.Build(
            [new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/png")])], null, mediaMarker: null));

    [Fact]
    public void ImagesBecomeMediaMarkers()
    {
        var prompt = PromptBuilder.Build(
            [new ChatMessage(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/png"), new TextContent("What is this?")])], null, "<__media__>");

        Assert.Single(prompt.Images);
        Assert.Equal("<__media__>\nWhat is this?", prompt.Messages[0]!["content"]!.GetValue<string>());
    }
}

public class VendoredJinjaTests
{
    private static string Render(string template, object bindings) =>
        AcDc.Jinja.Parser.Parse(template, AcDc.Jinja.Options.Default).Render(AcDc.Jinja.Context.Make(bindings, null!));

    [Theory]
    [InlineData("{{ s[:15] }}", "ab", "ab")]
    [InlineData("{{ s[5:] }}", "ab", "")]
    [InlineData("{{ s[-10:] }}", "ab", "ab")]
    [InlineData("{{ s[::-1] }}", "abc", "cba")]
    public void SlicesClampLikePython(string template, string value, string expected) =>
        Assert.Equal(expected, Render(template, new Dictionary<string, object> { ["s"] = value }));

    [Fact]
    public void AdjacentStringLiteralsJoin() =>
        Assert.Equal("ab c", Render("{{ 'a' \"b\"\n  ' c' }}", new Dictionary<string, object>()));

    [Fact]
    public void ToJsonEscapesLikeHuggingFace() =>
        Assert.Equal("\"say \\\"hi\\\" in Zürich\"", Render("{{ s | tojson }}", new Dictionary<string, object> { ["s"] = "say \"hi\" in Zürich" }));
}

public class TypedContentTests
{
    [Fact]
    public void TemplatesThatIterateContentGetTextParts()
    {
        var template = new ChatTemplate(
            "{% for m in messages %}{% for part in m['content'] %}{% if part['type'] == 'text' %}{{ part['text'] }}{% endif %}{% endfor %}{% endfor %}", "", "");

        Assert.True(template.RequiresTypedContent);
        Assert.Equal("Hello", template.Render([new JsonObject { ["role"] = "user", ["content"] = "Hello" }], null, false));
    }

    [Fact]
    public void StringTemplatesKeepStrings() =>
        Assert.False(new ChatTemplate(null, "", "").RequiresTypedContent);
}

public class GemmaTemplateTests
{
    [Fact]
    public void Gemma4RendersConversationsAndTools()
    {
        var template = new ChatTemplate(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", "google_gemma-4-E2B-it.jinja")), "<bos>", "<eos>");
        var prompt = PromptBuilder.Build(
            [new ChatMessage(ChatRole.System, "Be brief."), new ChatMessage(ChatRole.User, "Weather in Oslo?")],
            new ChatOptions { Tools = [AIFunctionFactory.Create((string city) => "", "get_weather", "Get the weather")] },
            mediaMarker: "<__media__>");

        var text = template.Render(prompt.Messages, prompt.Tools, enableThinking: false);

        Assert.Contains("Weather in Oslo?", text, StringComparison.Ordinal);
        Assert.Contains("get_weather", text, StringComparison.Ordinal);
    }
}
