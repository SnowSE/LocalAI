using Microsoft.Extensions.AI;

namespace LocalAI;

/// <summary>How a local chat model is loaded.</summary>
public sealed class LocalChatModelOptions
{
    /// <summary>Offload the model to the GPU (Vulkan, CUDA or Metal) when one is available. Defaults to <c>true</c>.</summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>Tokens of conversation each session can hold. Defaults to 4096.</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>
    /// The multimodal projection file (<c>mmproj</c>) for a vision model, as a path or <c>hf://</c>
    /// source. Without it, images in a prompt are an error.
    /// </summary>
    public string? ProjectionSource { get; set; }

    /// <summary>
    /// How many requests can generate at once. Each gets its own context, which costs memory, and
    /// keeps the conversation it last served cached so the next turn only processes what's new.
    /// Defaults to 2.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>Reply length when a request doesn't set <see cref="ChatOptions.MaxOutputTokens"/>. Defaults to 2048.</summary>
    public int DefaultMaxOutputTokens { get; set; } = 2048;
}

/// <summary>
/// Local-model settings that <see cref="ChatOptions"/> has no property for. They travel in
/// <see cref="ChatOptions.AdditionalProperties"/>, so hosted providers simply ignore them.
/// </summary>
public static class LocalChatOptionsExtensions
{
    /// <summary>Key for a regular expression the whole reply must match.</summary>
    public const string RegexKey = "localai.regex";

    /// <summary>Key for a GBNF grammar (root rule <c>root</c>) the reply must follow.</summary>
    public const string GrammarKey = "localai.grammar";

    /// <summary>Key for a min-p sampling threshold.</summary>
    public const string MinPKey = "localai.min_p";

    /// <summary>Key for a repetition penalty.</summary>
    public const string RepeatPenaltyKey = "localai.repeat_penalty";

    /// <summary>Key in <see cref="UsageDetails.AdditionalCounts"/> for the model's context size, reported by local models.</summary>
    public const string ContextSizeKey = "localai.context_size";

    /// <summary>Constrain a local model's reply to match <paramref name="pattern"/>. Hosted models ignore this.</summary>
    public static ChatOptions WithRegex(this ChatOptions options, string pattern) => options.With(RegexKey, pattern);

    /// <summary>Constrain a local model's reply to a GBNF grammar. Hosted models ignore this.</summary>
    public static ChatOptions WithGrammar(this ChatOptions options, string gbnf) => options.With(GrammarKey, gbnf);

    /// <summary>Set min-p sampling for a local model. Hosted models ignore this.</summary>
    public static ChatOptions WithMinP(this ChatOptions options, float minP) => options.With(MinPKey, minP);

    /// <summary>Set a repetition penalty for a local model. Hosted models ignore this.</summary>
    public static ChatOptions WithRepeatPenalty(this ChatOptions options, float penalty) => options.With(RepeatPenaltyKey, penalty);

    /// <summary>
    /// Turn a reasoning model's thinking on or off. This uses <see cref="ChatOptions.Reasoning"/>,
    /// so it works for hosted reasoning models too.
    /// </summary>
    public static ChatOptions WithThinking(this ChatOptions options, bool enabled)
    {
        options.Reasoning = new ReasoningOptions { Effort = enabled ? ReasoningEffort.Medium : ReasoningEffort.None };
        return options;
    }

    /// <summary>The context size a local model reported with this usage, if any.</summary>
    public static long? ContextSize(this UsageDetails usage) =>
        usage.AdditionalCounts is { } counts && counts.TryGetValue(ContextSizeKey, out var size) ? size : null;

    private static ChatOptions With(this ChatOptions options, string key, object value)
    {
        (options.AdditionalProperties ??= [])[key] = value;
        return options;
    }

    internal static T? Get<T>(this ChatOptions? options, string key) =>
        options?.AdditionalProperties is { } properties && properties.TryGetValue(key, out var value) && value is T typed ? typed : default;
}
