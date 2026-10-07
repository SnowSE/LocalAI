using System.ComponentModel;
using LocalAI.Audio;
using Microsoft.Extensions.AI;

namespace LocalAI.Tests;

/// <summary>
/// Runs real models. Opt in with <c>LOCALAI_INTEGRATION=1</c>; the first run downloads about 1 GB.
/// Override a model with <c>LOCALAI_CHAT_MODEL</c> and similar.
/// </summary>
public sealed class ModelFixture : IAsyncLifetime
{
    public static bool Enabled => Environment.GetEnvironmentVariable("LOCALAI_INTEGRATION") == "1";

    private static string Source(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback;

    public LocalChatClient Chat { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (Enabled)
            Chat = await LocalChatClient.LoadAsync(Source("LOCALAI_CHAT_MODEL", "hf://NobodyWho/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf"));
    }

    public Task<LocalEmbeddingGenerator> Embeddings() =>
        LocalEmbeddingGenerator.LoadAsync(Source("LOCALAI_EMBEDDING_MODEL", "hf://CompendiumLabs/bge-small-en-v1.5-gguf/bge-small-en-v1.5-q8_0.gguf"));

    public Task<LocalReranker> Reranker() =>
        LocalReranker.LoadAsync(Source("LOCALAI_RERANKER_MODEL", "hf://gpustack/bge-reranker-v2-m3-GGUF/bge-reranker-v2-m3-Q8_0.gguf"));

    public Task<LocalSpeechToTextClient> SpeechToText() =>
        LocalSpeechToTextClient.LoadAsync(Source("LOCALAI_STT_MODEL", "hf://ggerganov/whisper.cpp/ggml-base.bin"));

    public Task<LocalTextToSpeechClient> TextToSpeech() =>
        LocalTextToSpeechClient.LoadAsync(Environment.GetEnvironmentVariable("LOCALAI_TTS_MODEL"));

    public ValueTask DisposeAsync()
    {
        Chat?.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!ModelFixture.Enabled)
            Skip = "Set LOCALAI_INTEGRATION=1 to run tests against real models.";
    }
}

public class ChatIntegrationTests(ModelFixture models) : IClassFixture<ModelFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ChatOptions NoThinking() => new ChatOptions { Temperature = 0 }.WithThinking(false);

    [IntegrationFact]
    public async Task StreamsAnAnswer()
    {
        var text = "";
        UsageDetails? usage = null;
        await foreach (var update in models.Chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "What is the capital of France? One word.")], NoThinking(), Token))
        {
            text += update.Text;
            usage ??= update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
        }

        Assert.Contains("Paris", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(usage);
        Assert.Equal(models.Chat.ContextSize, usage.ContextSize());
    }

    [IntegrationFact]
    public async Task ThinkingComesBackAsReasoning()
    {
        var response = await models.Chat.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is 2+2?")],
            new ChatOptions { Temperature = 0, MaxOutputTokens = 400 }.WithThinking(true), Token);

        Assert.Contains(response.Messages.SelectMany(m => m.Contents), c => c is TextReasoningContent);
        Assert.DoesNotContain("<think>", response.Text, StringComparison.Ordinal);
    }

    [IntegrationFact]
    public async Task ReusesTheCacheForTheNextTurn()
    {
        List<ChatMessage> history = [new(ChatRole.System, "You are terse."), new(ChatRole.User, "Name a colour.")];
        var first = await models.Chat.GetResponseAsync(history, NoThinking(), Token);
        history.AddMessages(first);
        history.Add(new ChatMessage(ChatRole.User, "Another one."));

        var second = await models.Chat.GetResponseAsync(history, NoThinking(), Token);

        Assert.False(string.IsNullOrWhiteSpace(second.Text));
    }

    [IntegrationFact]
    public async Task CallsToolsThroughFunctionInvocation()
    {
        var calls = new List<string>();
        var weather = AIFunctionFactory.Create(
            ([Description("The city")] string city) =>
            {
                calls.Add(city);
                return $"{{\"city\": \"{city}\", \"temperature\": 21, \"sky\": \"sunny\"}}";
            },
            "get_weather", "Get the current weather for a city");
        // Not disposed: disposing the wrapper would dispose the shared model under it.
        var client = new ChatClientBuilder(models.Chat).UseFunctionInvocation().Build();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What's the weather in Oslo?")],
            new ChatOptions { Tools = [weather] }.WithThinking(false), Token);

        Assert.Equal(["Oslo"], calls);
        Assert.Contains("21", response.Text, StringComparison.Ordinal);
    }

    public sealed record Contact(string Name, string Email, List<string> Topics);

    [IntegrationFact]
    public async Task ProducesJsonMatchingASchema()
    {
        var response = await models.Chat.GetResponseAsync<Contact>(
            "Extract the contact: 'Hi, I'm Dana Reyes (dana@example.com). Asking about invoices and the API.'",
            NoThinking(), cancellationToken: Token);

        Assert.True(response.TryGetResult(out var contact), response.Text);
        Assert.Equal("dana@example.com", contact.Email);
        Assert.NotEmpty(contact.Topics);
    }

    [IntegrationFact]
    public async Task FollowsARegex()
    {
        var response = await models.Chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "Label the sentiment of the review."), new ChatMessage(ChatRole.User, "Terrible. It broke on day one.")],
            NoThinking().WithRegex("(positive|negative|mixed)"), Token);

        Assert.Equal("negative", response.Text);
    }

    [IntegrationFact]
    public async Task StopsWhenCancelled()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in models.Chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Write a long story.")], NoThinking(), cts.Token))
            {
                if (++count == 5)
                    await cts.CancelAsync();
            }
        });

        // The client is still usable afterwards.
        var after = await models.Chat.GetResponseAsync([new ChatMessage(ChatRole.User, "Say hi.")], NoThinking(), Token);
        Assert.False(string.IsNullOrWhiteSpace(after.Text));
    }
}

public class SearchIntegrationTests(ModelFixture models) : IClassFixture<ModelFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [IntegrationFact]
    public async Task EmbeddingsFindTheRelatedDocument()
    {
        using var embeddings = await models.Embeddings();
        string[] documents = ["New laptops are ordered through the IT portal.", "Parking passes are handed out on Mondays."];
        var vectors = await embeddings.GenerateAsync(documents, cancellationToken: Token);
        var query = await embeddings.GenerateVectorAsync("How do I get a new computer?", cancellationToken: Token);

        var scores = vectors.Select(v => System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(query.Span, v.Vector.Span)).ToArray();
        Assert.True(scores[0] > scores[1], $"Scores: {string.Join(", ", scores)}");
    }

    [IntegrationFact]
    public async Task RerankerPutsTheAnswerFirst()
    {
        using var reranker = await models.Reranker();
        var ranked = await reranker.RerankAsync("What is the capital of France?",
            ["The capital of Germany is Berlin.", "Paris is the capital of France.", "Bananas are yellow."], Token);

        Assert.Equal([1, 0, 2], ranked.Select(r => r.Index));
        Assert.InRange(ranked[0].Score, 0.9f, 1f);
        Assert.InRange(ranked[2].Score, 0f, 0.1f);
    }
}

public class SpeechIntegrationTests(ModelFixture models) : IClassFixture<ModelFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [IntegrationFact]
    public async Task SpeechRoundTrips()
    {
        using var tts = await models.TextToSpeech();
        var audio = await tts.GetAudioAsync("The quick brown fox jumps over the lazy dog.", new TextToSpeechOptions { VoiceId = "am_michael" }, Token);
        var wav = audio.Contents.OfType<DataContent>().Single();
        Assert.Equal("audio/wav", wav.MediaType);

        using var vad = await SileroVoiceActivityDetector.LoadAsync(cancellationToken: Token);
        var segments = await vad.DetectAsync(AudioData.Decode(wav.Data.Span), cancellationToken: Token);
        Assert.NotEmpty(segments);

        using var stt = await models.SpeechToText();
        var text = await stt.GetTextAsync(new MemoryStream(wav.Data.ToArray()), new SpeechToTextOptions { SpeechLanguage = "en" }, Token);
        Assert.Contains("fox", text.Text, StringComparison.OrdinalIgnoreCase);
    }
}

public class VisionIntegrationTests
{
    [IntegrationFact]
    public async Task ReadsAnImage()
    {
        var token = TestContext.Current.CancellationToken;
        using var vision = await LocalChatClient.LoadAsync(
            Environment.GetEnvironmentVariable("LOCALAI_VISION_MODEL") is { Length: > 0 } model ? model : "hf://ggml-org/SmolVLM-256M-Instruct-GGUF/SmolVLM-256M-Instruct-Q8_0.gguf",
            new LocalChatModelOptions
            {
                ProjectionSource = Environment.GetEnvironmentVariable("LOCALAI_VISION_PROJECTION") is { Length: > 0 } projection ? projection : "hf://ggml-org/SmolVLM-256M-Instruct-GGUF/mmproj-SmolVLM-256M-Instruct-Q8_0.gguf",
            },
            cancellationToken: token);
        Assert.True(vision.SupportsVision);

        var image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Images", "screenshot.png"), token);
        var response = await vision.GetResponseAsync(
            [new ChatMessage(ChatRole.User, [new DataContent(image, "image/png"), new TextContent("What is the big heading at the top of this screenshot?")])],
            new ChatOptions { Temperature = 0, MaxOutputTokens = 60 }.WithThinking(false), token);

        // The screenshot's headings are the "LocalAI" brand and the page title "Chat".
        Assert.True(response.Text.Contains("LocalAI", StringComparison.OrdinalIgnoreCase) || response.Text.Contains("Chat", StringComparison.OrdinalIgnoreCase), response.Text);

        // A text-only turn afterwards still works on the same client.
        var after = await vision.GetResponseAsync([new ChatMessage(ChatRole.User, "Say hello.")], new ChatOptions { MaxOutputTokens = 20 }, token);
        Assert.False(string.IsNullOrWhiteSpace(after.Text));
    }
}
