using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using LocalAI.Grammar;
using LocalAI.Llama;
using Microsoft.Extensions.AI;

namespace LocalAI;

/// <summary>
/// A GGUF chat model loaded into this process, used through <see cref="IChatClient"/>. Streaming,
/// tool calling (with <see cref="FunctionInvokingChatClient"/>), JSON-schema output, reasoning and
/// images (with a projection file) all work the way they do with a hosted model.
/// </summary>
/// <remarks>
/// <para>
/// Like every <see cref="IChatClient"/>, this one is stateless: each request carries the whole
/// conversation. To keep multi-turn chat fast it keeps a few contexts alive and serves each request
/// from the one whose cached tokens share the longest prefix with the new prompt, so usually only
/// the newest message has to be processed.
/// </para>
/// <para>Create one per model and share it; it is safe to use from many threads at once.</para>
/// </remarks>
public sealed class LocalChatClient : IChatClient
{
    private readonly LLamaWeights _weights;
    private readonly MtmdWeights? _vision;
    private readonly ModelParams _params;
    private readonly LocalChatModelOptions _options;
    private readonly ChatTemplate _template;
    private readonly ChatClientMetadata _metadata;
    private readonly SemaphoreSlim _slots;
    private readonly List<Session> _sessions = [];
    private readonly Lock _lock = new();
    private bool _disposed;

    private LocalChatClient(LLamaWeights weights, MtmdWeights? vision, ModelParams @params, LocalChatModelOptions options, string source)
    {
        _weights = weights;
        _vision = vision;
        _params = @params;
        _options = options;
        _slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRequests));

        var vocab = weights.Vocab;
        _template = new ChatTemplate(
            weights.Metadata.GetValueOrDefault("tokenizer.chat_template"),
            TokenText(vocab.BOS),
            TokenText(vocab.EOS ?? vocab.EOT));
        ModelName = weights.Metadata.GetValueOrDefault("general.name") ?? Path.GetFileNameWithoutExtension(source);
        _metadata = new ChatClientMetadata("LocalAI", null, ModelName);
    }

    /// <summary>The model's name, from its metadata.</summary>
    public string ModelName { get; }

    /// <summary>Tokens of conversation each request can use.</summary>
    public int ContextSize => (int)(_params.ContextSize ?? 4096);

    /// <summary>Whether this model can read images.</summary>
    public bool SupportsVision => _vision?.SupportsVision == true;

    /// <summary>Whether the model's template has a thinking mode that <see cref="ChatOptions.Reasoning"/> can turn on or off.</summary>
    public bool SupportsThinking => _template.SupportsThinking;

    /// <summary>
    /// Load a GGUF chat model from a path or an <c>hf://owner/repo/file.gguf</c> source, downloading it
    /// first if needed.
    /// </summary>
    public static async Task<LocalChatClient> LoadAsync(
        string source,
        LocalChatModelOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LocalChatModelOptions();
        var path = await ModelSource.ResolveAsync(source, progress, cancellationToken).ConfigureAwait(false);
        var projection = string.IsNullOrWhiteSpace(options.ProjectionSource)
            ? null
            : await ModelSource.ResolveAsync(options.ProjectionSource, progress, cancellationToken).ConfigureAwait(false);

        LlamaBackend.EnsureConfigured(options.UseGpu);
        var @params = new ModelParams(path)
        {
            ContextSize = (uint)options.ContextSize,
            GpuLayerCount = options.UseGpu ? -1 : 0,
            BatchSize = 512,
            UBatchSize = 512,
            FlashAttention = null,
        };
        var weights = await LLamaWeights.LoadFromFileAsync(@params, cancellationToken).ConfigureAwait(false);
        MtmdWeights? vision = null;
        try
        {
            if (projection is not null)
            {
                var mtmd = MtmdContextParams.Default();
                mtmd.UseGpu = options.UseGpu;
                vision = await MtmdWeights.LoadFromFileAsync(projection, weights, mtmd, cancellationToken).ConfigureAwait(false);
            }
            return new LocalChatClient(weights, vision, @params, options, source);
        }
        catch
        {
            vision?.Dispose();
            weights.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mediaMarker = _vision is null ? null : NativeApi.MtmdDefaultMarker() ?? "<__media__>";
        var prompt = PromptBuilder.Build(messages, options, mediaMarker);
        var plan = Plan(prompt, options);

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        Session? session = null;
        try
        {
            var (text, tokens) = Fit(prompt, plan);
            session = Acquire(tokens, prompt.Images.Count > 0);
            var responseId = Guid.NewGuid().ToString("N");
            var messageId = Guid.NewGuid().ToString("N");
            ChatResponseUpdate Update(AIContent content) => new(ChatRole.Assistant, [content])
            {
                ResponseId = responseId,
                MessageId = messageId,
                ModelId = ModelName,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            if (prompt.Images.Count > 0)
                await EvaluateWithImagesAsync(session, text, prompt.Images, cancellationToken).ConfigureAwait(false);
            else
                await EvaluateAsync(session, tokens, cancellationToken).ConfigureAwait(false);
            var promptTokens = session.Position;

            var parser = new OutputParser(plan.StartsInReasoning, plan.Tools.Count > 0, _template.ReasoningOpen, _template.ReasoningClose);
            if (plan.StartsInToolCall)
                parser.BeginToolCall();

            var maxTokens = options?.MaxOutputTokens ?? _options.DefaultMaxOutputTokens;
            // Special tokens such as <think> and <tool_call> carry meaning here, so render them.
            var decoder = new StreamingTokenDecoder(Encoding.UTF8, _weights) { DecodeSpecialTokens = true };
            var stops = options?.StopSequences?.Where(s => s.Length > 0).ToArray() ?? [];
            var visible = new StringBuilder();
            var calls = 0;
            var generated = 0;
            var finish = ChatFinishReason.Length;

            var free = CreateSampler(options, plan.Grammar);
            var sampler = plan.StartsInToolCall ? CreateSampler(options, ToolCallGrammar.ForCallBody(plan.Tools, ChatTemplate.ToolCallClose)) : free;
            try
            {
                while (generated < maxTokens)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var token = sampler.Sample(session.Context.NativeHandle, session.LogitsIndex);
                    if (token.IsEndOfGeneration(_weights.Vocab))
                    {
                        finish = calls > 0 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop;
                        break;
                    }

                    generated++;
                    await DecodeTokenAsync(session, token, cancellationToken).ConfigureAwait(false);

                    decoder.Add(token);
                    var piece = decoder.Read();
                    if (piece.Length == 0)
                        continue;

                    var wasInCall = parser.Current == OutputParser.State.ToolCall;
                    var stop = false;
                    foreach (var output in parser.Push(piece))
                    {
                        switch (output.Kind)
                        {
                            case OutputKind.Reasoning:
                                yield return Update(new TextReasoningContent(output.Text));
                                break;
                            case OutputKind.Text:
                                var (emit, hit) = ApplyStops(visible, output.Text, stops);
                                if (emit.Length > 0)
                                    yield return Update(new TextContent(emit));
                                stop |= hit;
                                break;
                            case OutputKind.ToolCall:
                                calls++;
                                yield return Update(ParseCall(output.Text, responseId, calls));
                                break;
                        }
                    }
                    if (stop)
                    {
                        finish = ChatFinishReason.Stop;
                        break;
                    }

                    // Constrain the call's body to the tools' schemas while the model is inside one.
                    var inCall = parser.Current == OutputParser.State.ToolCall;
                    if (inCall && !wasInCall)
                    {
                        sampler = CreateSampler(options, ToolCallGrammar.ForCallBody(plan.Tools, ChatTemplate.ToolCallClose));
                    }
                    else if (!inCall && wasInCall)
                    {
                        sampler.Dispose();
                        sampler = free;
                        if (options?.AllowMultipleToolCalls == false)
                        {
                            finish = ChatFinishReason.ToolCalls;
                            break;
                        }
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(sampler, free))
                    sampler.Dispose();
                free.Dispose();
            }

            foreach (var output in parser.Finish())
            {
                if (output.Kind == OutputKind.Reasoning)
                    yield return Update(new TextReasoningContent(output.Text));
                else if (output.Kind == OutputKind.Text && ApplyStops(visible, output.Text, stops).Emit is { Length: > 0 } rest)
                    yield return Update(new TextContent(rest));
                else if (output.Kind == OutputKind.ToolCall)
                    yield return Update(ParseCall(output.Text, responseId, ++calls));
            }

            if (calls > 0 && finish == ChatFinishReason.Stop)
                finish = ChatFinishReason.ToolCalls;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails
            {
                InputTokenCount = promptTokens,
                OutputTokenCount = generated,
                TotalTokenCount = promptTokens + generated,
                AdditionalCounts = new() { [LocalChatOptionsExtensions.ContextSizeKey] = ContextSize },
            })])
            {
                ResponseId = responseId,
                MessageId = messageId,
                ModelId = ModelName,
                FinishReason = finish,
            };
        }
        finally
        {
            Release(session);
            _slots.Release();
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(ChatClientMetadata))
            return _metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var session in _sessions)
                session.Context.Dispose();
            _sessions.Clear();
        }
        _vision?.Dispose();
        _weights.Dispose();
        _slots.Dispose();
    }

    // ---- Planning: how this request will be generated --------------------------------------

    private sealed record GenerationPlan(bool EnableThinking, string? Grammar, IReadOnlyList<ToolSignature> Tools, bool StartsInToolCall)
    {
        public bool StartsInReasoning { get; set; }
    }

    private GenerationPlan Plan(Prompt prompt, ChatOptions? options)
    {
        string? grammar = null;
        if (options.Get<string>(LocalChatOptionsExtensions.GrammarKey) is { Length: > 0 } gbnf)
            grammar = gbnf;
        else if (options.Get<string>(LocalChatOptionsExtensions.RegexKey) is { Length: > 0 } regex)
            grammar = RegexGrammar.FromPattern(regex);
        else if (options?.ResponseFormat is ChatResponseFormatJson json)
            grammar = JsonSchemaGrammar.FromSchema(json.Schema ?? JsonDocument.Parse("""{"type":"object"}""").RootElement);

        var thinking = _template.SupportsThinking && options?.Reasoning?.Effort is not ReasoningEffort.None;
        // A grammar constrains the reply from its first token, which leaves no room to think.
        if (grammar is not null)
            thinking = false;

        var tools = grammar is null ? prompt.ToolSignatures : [];
        var forceCall = tools.Count > 0 && options?.ToolMode is RequiredChatToolMode;
        return new GenerationPlan(thinking, grammar, tools, forceCall);
    }

    /// <summary>Render and tokenize the prompt, dropping the oldest turns if it doesn't fit.</summary>
    private (string Text, LLamaToken[] Tokens) Fit(Prompt prompt, GenerationPlan plan)
    {
        var messages = prompt.Messages;
        var reserve = Math.Min(_options.DefaultMaxOutputTokens, ContextSize / 4);
        while (true)
        {
            var text = _template.Render(messages, plan.Tools.Count > 0 ? prompt.Tools : null, plan.EnableThinking);
            if (plan.StartsInToolCall)
                text += ChatTemplate.ToolCallOpen + "\n";
            plan.StartsInReasoning = text.TrimEnd().EndsWith(_template.ReasoningOpen, StringComparison.Ordinal);

            var tokens = Tokenize(text);
            if (tokens.Length + reserve <= ContextSize || prompt.Images.Count > 0)
                return (text, tokens);

            // Drop the oldest message after the system prompt, keeping at least the last one.
            var first = messages.Count > 0 && messages[0]?["role"]?.GetValue<string>() == "system" ? 1 : 0;
            if (messages.Count - first <= 1)
                throw new InvalidOperationException($"The prompt is {tokens.Length} tokens, more than this model's context of {ContextSize} allows. Load the model with a larger LocalChatModelOptions.ContextSize.");
            messages = JsonNode.Parse(messages.ToJsonString())!.AsArray();
            messages.RemoveAt(first);
            // A tool result without the call before it confuses templates; drop orphans too.
            while (messages.Count - first > 1 && messages[first]?["role"]?.GetValue<string>() == "tool")
                messages.RemoveAt(first);
        }
    }

    private LLamaToken[] Tokenize(string text)
    {
        var addBos = _weights.Vocab.ShouldAddBOS && !(text.StartsWith(_template.BosToken, StringComparison.Ordinal) && _template.BosToken.Length > 0);
        return _weights.Tokenize(text, addBos, special: true, Encoding.UTF8);
    }

    // ---- Sessions: contexts kept alive between requests -------------------------------------

    private sealed class Session(LLamaContext context)
    {
        public LLamaContext Context { get; } = context;
        public List<LLamaToken> Tokens { get; } = [];
        public bool Busy { get; set; }
        /// <summary>The prompt included images, so <see cref="Tokens"/> doesn't describe the cache.</summary>
        public bool Opaque { get; set; }
        public int Position { get; set; }
        public int LogitsIndex { get; set; }
        public LLamaBatch Batch { get; } = new();
    }

    private Session Acquire(LLamaToken[] tokens, bool hasImages)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Session? best = null;
            var bestShared = -1;
            foreach (var session in _sessions.Where(s => !s.Busy))
            {
                var shared = session.Opaque || hasImages ? 0 : CommonPrefix(session.Tokens, tokens);
                if (shared > bestShared)
                    (best, bestShared) = (session, shared);
            }
            // Open another context when the free ones share nothing and there's room for one.
            if (best is null || (bestShared == 0 && _sessions.Count < _options.MaxConcurrentRequests))
            {
                best = new Session(_weights.CreateContext(_params));
                _sessions.Add(best);
            }
            best.Busy = true;
            return best;
        }
    }

    private void Release(Session? session)
    {
        if (session is null)
            return;
        lock (_lock)
            session.Busy = false;
    }

    private static int CommonPrefix(List<LLamaToken> cached, LLamaToken[] tokens)
    {
        var n = Math.Min(cached.Count, tokens.Length);
        var i = 0;
        while (i < n && cached[i] == tokens[i])
            i++;
        return i;
    }

    // ---- Evaluation ------------------------------------------------------------------------

    private async Task EvaluateAsync(Session session, LLamaToken[] tokens, CancellationToken cancellationToken)
    {
        var handle = session.Context.NativeHandle;
        var reuse = session.Opaque ? 0 : CommonPrefix(session.Tokens, tokens);
        // The last prompt token must be evaluated again to get fresh logits.
        if (reuse == tokens.Length)
            reuse--;

        if (reuse > 0)
        {
            handle.MemorySequenceRemove(LLamaSeqId.Zero, reuse, -1);
            // Some architectures (sliding-window or recurrent) can't drop part of the cache; start over then.
            if ((int)handle.MemorySequenceMaxPosition(LLamaSeqId.Zero) != reuse - 1)
                reuse = 0;
        }
        if (reuse == 0)
            handle.MemoryClear();

        session.Tokens.RemoveRange(reuse, session.Tokens.Count - reuse);
        session.Opaque = false;
        session.Position = reuse;

        var batchSize = (int)session.Context.BatchSize;
        for (var start = reuse; start < tokens.Length; start += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(batchSize, tokens.Length - start);
            session.Batch.Clear();
            session.Batch.AddRange(tokens.AsSpan(start, count), start, LLamaSeqId.Zero, logitsLast: start + count == tokens.Length);
            await DecodeAsync(session, cancellationToken).ConfigureAwait(false);
            session.Tokens.AddRange(tokens.AsSpan(start, count));
            session.Position = start + count;
            session.LogitsIndex = count - 1;
        }
    }

    private async Task EvaluateWithImagesAsync(Session session, string text, IReadOnlyList<DataContent> images, CancellationToken cancellationToken)
    {
        var vision = _vision ?? throw new NotSupportedException("This model can't read images.");
        var handle = session.Context.NativeHandle;
        handle.MemoryClear();
        session.Tokens.Clear();
        session.Opaque = true;

        var embeds = new List<SafeMtmdEmbed>();
        try
        {
            foreach (var image in images)
                embeds.Add(vision.LoadMedia(image.Data.Span));

            var addBos = _weights.Vocab.ShouldAddBOS && !(_template.BosToken.Length > 0 && text.StartsWith(_template.BosToken, StringComparison.Ordinal));
            if (vision.Tokenize(text, addBos, parseSpecial: true, embeds.ToArray(), out var chunks) != 0 || chunks is null)
                throw new InvalidOperationException("The prompt and its images couldn't be tokenized.");
            using (chunks)
            {
                var position = 0;
                var result = await Task.Run(() => vision.EvaluateChunks(chunks, handle, ref position, 0, (int)session.Context.BatchSize, logitsLast: true), cancellationToken).ConfigureAwait(false);
                if (result != 0)
                    throw new InvalidOperationException($"Evaluating the images failed (error {result}).");
                session.Position = position;
                // mtmd leaves the logits of the last token at the end of its final batch.
                session.LogitsIndex = -1;
            }
        }
        finally
        {
            foreach (var embed in embeds)
                embed.Dispose();
        }
    }

    private async Task DecodeTokenAsync(Session session, LLamaToken token, CancellationToken cancellationToken)
    {
        if (session.Position >= ContextSize)
            throw new InvalidOperationException($"The conversation filled this model's context of {ContextSize} tokens.");
        session.Batch.Clear();
        session.Batch.Add(token, session.Position, LLamaSeqId.Zero, true);
        await DecodeAsync(session, cancellationToken).ConfigureAwait(false);
        if (!session.Opaque)
            session.Tokens.Add(token);
        session.Position++;
        session.LogitsIndex = 0;
    }

    private static async Task DecodeAsync(Session session, CancellationToken cancellationToken)
    {
        var result = await session.Context.DecodeAsync(session.Batch, cancellationToken).ConfigureAwait(false);
        if (result != DecodeResult.Ok)
            throw new InvalidOperationException($"The model failed to process the prompt ({result}).");
    }

    // ---- Sampling and output ---------------------------------------------------------------

    private static DefaultSamplingPipeline CreateSampler(ChatOptions? options, string? grammar) => new()
    {
        Temperature = options?.Temperature ?? 0.7f,
        TopP = options?.TopP ?? 0.9f,
        TopK = options?.TopK ?? 40,
        MinP = options.Get<float?>(LocalChatOptionsExtensions.MinPKey) ?? 0.05f,
        RepeatPenalty = options.Get<float?>(LocalChatOptionsExtensions.RepeatPenaltyKey) ?? 1f,
        FrequencyPenalty = options?.FrequencyPenalty ?? 0f,
        PresencePenalty = options?.PresencePenalty ?? 0f,
        Seed = options?.Seed is { } seed ? unchecked((uint)seed) : (uint)Random.Shared.Next(),
        Grammar = grammar is null ? null : new LLama.Sampling.Grammar(grammar, "root"),
    };

    /// <summary>Append <paramref name="text"/> to the reply, cutting it at the first stop sequence.</summary>
    private static (string Emit, bool Hit) ApplyStops(StringBuilder visible, string text, string[] stops)
    {
        if (stops.Length == 0)
        {
            visible.Append(text);
            return (text, false);
        }
        var before = visible.Length;
        visible.Append(text);
        var all = visible.ToString();
        foreach (var stop in stops)
        {
            var at = all.IndexOf(stop, Math.Max(0, before - stop.Length), StringComparison.Ordinal);
            if (at >= 0)
            {
                visible.Length = at;
                return (at > before ? all[before..at] : "", true);
            }
        }
        return (text, false);
    }

    private static AIContent ParseCall(string body, string responseId, int index)
    {
        var callId = $"call_{responseId[..8]}_{index}";
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var name = root.GetProperty("name").GetString() ?? "";
            var arguments = root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
                ? args.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
                : [];
            return new FunctionCallContent(callId, name, arguments);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Grammar-constrained calls are always valid; this only happens when generation was cut short.
            return new ErrorContent($"The model produced an incomplete tool call: {body}");
        }
    }

    private string TokenText(LLamaToken? token) =>
        token is { } t ? _weights.Vocab.LLamaTokenToString(t, isSpecialToken: true) ?? "" : "";
}
