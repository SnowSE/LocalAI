using System.Text.Json;
using System.Text.Json.Nodes;
using AcDc.Jinja;

namespace LocalAI.Llama;

/// <summary>
/// A model's own chat template (the Jinja template in its GGUF metadata), rendered the way the
/// model was trained to see conversations, tools included.
/// </summary>
internal sealed class ChatTemplate
{
    /// <summary>ChatML, for models whose GGUF carries no template.</summary>
    private const string ChatMl = """
        {%- for message in messages -%}
        {{- '<|im_start|>' + message.role + '\n' + message.content + '<|im_end|>\n' -}}
        {%- endfor -%}
        {%- if add_generation_prompt -%}{{- '<|im_start|>assistant\n' -}}{%- endif -%}
        """;

    private readonly TemplateNode _root;

    public ChatTemplate(string? source, string bosToken, string eosToken)
    {
        Source = string.IsNullOrWhiteSpace(source) ? ChatMl : source;
        _root = Parser.Parse(Source, new Options { TrimBlocks = true, LStripBlocks = true, KeepTrailingNewline = false });
        BosToken = bosToken;
        EosToken = eosToken;

        // Tool calls are always parsed in the Hermes format (<tool_call>{json}</tool_call>), which
        // Qwen, Hermes and many other open models were trained on. Templates in that format render
        // the tools themselves. Models with their own format (Gemma, Llama) get the tools described
        // in the system prompt instead, so grammar-constrained calling works for every model.
        SupportsTools = Source.Contains(ToolCallOpen, StringComparison.Ordinal);

        // Reasoning comes in <think> tags, or in Gemma 4's thought channel.
        (ReasoningOpen, ReasoningClose) = Source.Contains(GemmaThoughtOpen, StringComparison.Ordinal)
            ? (GemmaThoughtOpen, GemmaThoughtClose)
            : (ThinkOpen, ThinkClose);
        SupportsThinking = Source.Contains("enable_thinking", StringComparison.Ordinal) || Source.Contains(ReasoningOpen, StringComparison.Ordinal);
        RequiresTypedContent = !RendersStringContent();
    }

    public string Source { get; }
    public string BosToken { get; }
    public string EosToken { get; }
    /// <summary>Whether the template renders tools and calls in the Hermes format the client parses.</summary>
    public bool SupportsTools { get; }
    public bool SupportsThinking { get; }

    /// <summary>The tags around this model's reasoning.</summary>
    public string ReasoningOpen { get; }
    public string ReasoningClose { get; }

    /// <summary>
    /// Whether the template only understands content as a list of parts
    /// (<c>[{"type": "text", "text": …}]</c>), as many vision models' templates do.
    /// </summary>
    public bool RequiresTypedContent { get; }

    public const string ToolCallOpen = "<tool_call>";
    public const string ToolCallClose = "</tool_call>";
    public const string ThinkOpen = "<think>";
    public const string ThinkClose = "</think>";
    public const string GemmaThoughtOpen = "<|channel>thought";
    public const string GemmaThoughtClose = "<channel|>";
    public const string ToolResponseOpen = "<tool_response>";
    public const string ToolResponseClose = "</tool_response>";

    /// <summary>Render <paramref name="messages"/> as a prompt that ends where the assistant's reply begins.</summary>
    /// <param name="messages">Messages in the template's shape: <c>role</c>, <c>content</c>, and <c>tool_calls</c> / <c>tool_call_id</c> where relevant.</param>
    /// <param name="tools">Tool definitions in OpenAI's <c>{"type": "function", "function": {…}}</c> shape, or <c>null</c>.</param>
    /// <param name="enableThinking">Whether a reasoning model should think before answering.</param>
    public string Render(JsonArray messages, JsonArray? tools, bool enableThinking)
    {
        if (!SupportsTools)
        {
            messages = WithHermesToolMessages(messages, tools);
            tools = null;
        }
        if (RequiresTypedContent)
            messages = WithTypedContent(messages);

        return RenderRaw(messages, tools, enableThinking);
    }

    private string RenderRaw(JsonArray messages, JsonArray? tools, bool enableThinking)
    {
        var context = new JsonObject
        {
            ["messages"] = messages,
            ["add_generation_prompt"] = true,
            ["bos_token"] = BosToken,
            ["eos_token"] = EosToken,
            ["enable_thinking"] = enableThinking,
        };
        if (tools is { Count: > 0 })
            context["tools"] = tools;

        try
        {
            return _root.Render(Context.Make(JsonSerializer.SerializeToElement(context), null!));
        }
        catch (JinjaException e)
        {
            throw new InvalidOperationException($"The model's chat template couldn't render this conversation: {e.Message}", e);
        }
    }

    /// <summary>Render a probe message as plain string content and see whether it comes through, as llama.cpp does.</summary>
    private bool RendersStringContent()
    {
        const string probe = "<__localai_probe__>";
        try
        {
            var messages = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = probe } };
            return RenderRaw(messages, null, enableThinking: false).Contains(probe, StringComparison.Ordinal);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Wrap each string content in a single text part. Images are already media markers inside the text.</summary>
    private static JsonArray WithTypedContent(JsonArray messages)
    {
        var copy = JsonNode.Parse(messages.ToJsonString())!.AsArray();
        foreach (var message in copy.OfType<JsonObject>())
        {
            if (message["content"] is JsonValue value && value.TryGetValue<string>(out var text))
                message["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
        }
        return copy;
    }

    /// <summary>
    /// For templates without Hermes tool support: describe the tools and the call format in the
    /// system message, and write earlier calls and results as Hermes-format text, so the history
    /// matches what the model is asked to produce.
    /// </summary>
    private static JsonArray WithHermesToolMessages(JsonArray messages, JsonArray? tools)
    {
        var hasTools = tools is { Count: > 0 };
        var hasToolHistory = messages.OfType<JsonObject>().Any(m => m["tool_calls"] is not null || m["role"]?.GetValue<string>() == "tool");
        if (!hasTools && !hasToolHistory)
            return messages;

        var copy = new JsonArray();
        foreach (var message in messages.OfType<JsonObject>())
        {
            var role = message["role"]?.GetValue<string>() ?? "user";
            var content = message["content"]?.GetValue<string>() ?? "";
            if (message["tool_calls"] is JsonArray calls)
            {
                foreach (var call in calls.OfType<JsonObject>())
                {
                    var function = call["function"];
                    var json = new JsonObject { ["name"] = function?["name"]?.GetValue<string>(), ["arguments"] = function?["arguments"]?.DeepClone() };
                    content += $"{ToolCallOpen}{json.ToJsonString()}{ToolCallClose}";
                }
            }
            if (role == "tool")
            {
                role = "user";
                content = $"{ToolResponseOpen}{content}{ToolResponseClose}";
            }

            // Many templates require turns to alternate, so merge neighbours with the same role.
            if (copy.Count > 0 && copy[^1]!["role"]!.GetValue<string>() == role)
                copy[^1]!["content"] = copy[^1]!["content"]!.GetValue<string>() + "\n" + content;
            else
                copy.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        if (hasTools)
        {
            var instructions =
                "You may call one or more functions to help answer. The functions are described here as JSON:\n" +
                string.Join("\n", tools!.Select(t => t!.ToJsonString())) +
                $"\n\nTo call a function, reply with {ToolCallOpen}{{\"name\": <function name>, \"arguments\": <arguments object>}}{ToolCallClose} and nothing else. " +
                $"Results come back as {ToolResponseOpen}...{ToolResponseClose}.";
            if (copy.Count > 0 && copy[0]!["role"]!.GetValue<string>() == "system")
                copy[0]!["content"] = copy[0]!["content"]!.GetValue<string>() + "\n\n" + instructions;
            else
                copy.Insert(0, new JsonObject { ["role"] = "system", ["content"] = instructions });
        }
        return copy;
    }
}
