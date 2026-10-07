using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalAI.Grammar;
using Microsoft.Extensions.AI;

namespace LocalAI.Llama;

/// <summary>A conversation turned into what the chat template and the vision encoder need.</summary>
internal sealed record Prompt(JsonArray Messages, JsonArray? Tools, IReadOnlyList<ToolSignature> ToolSignatures, IReadOnlyList<DataContent> Images);

/// <summary>Converts MEAI messages and options into the template's message shape.</summary>
internal static class PromptBuilder
{
    public static Prompt Build(IEnumerable<ChatMessage> messages, ChatOptions? options, string? mediaMarker)
    {
        var list = new JsonArray();
        var images = new List<DataContent>();

        var instructions = options?.Instructions;
        var history = messages.ToList();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            if (history.FirstOrDefault()?.Role == ChatRole.System)
                history[0] = new ChatMessage(ChatRole.System, history[0].Text + "\n\n" + instructions);
            else
                history.Insert(0, new ChatMessage(ChatRole.System, instructions));
        }

        foreach (var message in history)
        {
            if (message.Role == ChatRole.Tool)
            {
                foreach (var result in message.Contents.OfType<FunctionResultContent>())
                {
                    list.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.CallId,
                        ["content"] = ResultText(result.Result),
                    });
                }
                continue;
            }

            var text = new StringBuilder();
            JsonArray? calls = null;
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent t:
                        text.Append(t.Text);
                        break;
                    case DataContent data when data.HasTopLevelMediaType("image"):
                        if (mediaMarker is null)
                            throw new NotSupportedException("This model can't read images. Load a vision model with its projection file (LocalChatModelOptions.ProjectionSource).");
                        images.Add(data);
                        text.Append(mediaMarker).Append('\n');
                        break;
                    case FunctionCallContent call:
                        (calls ??= []).Add(new JsonObject
                        {
                            ["type"] = "function",
                            ["id"] = call.CallId,
                            ["function"] = new JsonObject
                            {
                                ["name"] = call.Name,
                                ["arguments"] = JsonSerializer.SerializeToNode(call.Arguments ?? new Dictionary<string, object?>()),
                            },
                        });
                        break;
                    // Reasoning from earlier turns is left out, as reasoning models expect.
                }
            }

            var entry = new JsonObject
            {
                ["role"] = message.Role.Value,
                ["content"] = text.ToString(),
            };
            if (calls is not null)
                entry["tool_calls"] = calls;
            list.Add(entry);
        }

        var signatures = new List<ToolSignature>();
        JsonArray? tools = null;
        if (options?.ToolMode is not NoneChatToolMode)
        {
            foreach (var function in options?.Tools?.OfType<AIFunctionDeclaration>() ?? [])
            {
                signatures.Add(new ToolSignature(function.Name, function.JsonSchema));
                (tools ??= []).Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = function.Name,
                        ["description"] = function.Description,
                        ["parameters"] = JsonNode.Parse(function.JsonSchema.GetRawText()),
                    },
                });
            }
        }
        if (options?.ToolMode is RequiredChatToolMode { RequiredFunctionName: { } required })
            signatures.RemoveAll(s => s.Name != required);

        return new Prompt(list, tools, signatures, images);
    }

    private static string ResultText(object? result) => result switch
    {
        null => "",
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? "",
        _ => JsonSerializer.Serialize(result),
    };
}
