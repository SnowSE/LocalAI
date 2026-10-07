using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace LocalAI.Hosting;

/// <summary>
/// The app's models, one slot per capability, each loaded once on first use and shared by every
/// caller. Registered as a singleton by <c>AddLocalAI</c>; inject it to show load progress, or to
/// load a model ahead of time.
/// </summary>
public sealed class LocalAIModels : IDisposable
{
    public LocalAIModels(IOptions<LocalAIOptions> options, ILoggerFactory? loggerFactory = null)
    {
        var o = Options = options.Value;
        LocalAIRuntime.Logger ??= loggerFactory?.CreateLogger("LocalAI.Native");
        if (!string.IsNullOrWhiteSpace(o.CacheDirectory))
            ModelSource.CacheDirectory = o.CacheDirectory;

        Chat = ChatSlot("Chat", "Chat, tools and structured output", o.Chat);
        Vision = ChatSlot("Vision", "Questions about images", o.Vision);
        Embeddings = Slot<IEmbeddingGenerator<string, Embedding<float>>>("Embeddings", "Semantic search", o.Embeddings,
            async (progress, ct) => await LocalEmbeddingGenerator.LoadAsync(o.Embeddings.Model, o.UseGpu, progress, ct).ConfigureAwait(false),
            m => m.Provider switch
            {
                ModelProvider.OpenAI => OpenAI(m).GetEmbeddingClient(m.Model).AsIEmbeddingGenerator(),
                ModelProvider.Ollama => new OllamaApiClient(OllamaUri(m), m.Model),
                _ => throw Unsupported(m, "embeddings"),
            });
        Reranker = Slot<IReranker>("Reranker", "Reordering search results", o.Reranker,
            async (progress, ct) => await LocalReranker.LoadAsync(o.Reranker.Model, o.UseGpu, progress, ct).ConfigureAwait(false),
            m => throw Unsupported(m, "reranking"));
        SpeechToText = Slot<ISpeechToTextClient>("Speech to text", "Transcribing audio", o.SpeechToText,
            async (progress, ct) => await LocalSpeechToTextClient.LoadAsync(o.SpeechToText.Model, o.UseGpu, progress, ct).ConfigureAwait(false),
            m => m.Provider == ModelProvider.OpenAI ? OpenAI(m).GetAudioClient(m.Model).AsISpeechToTextClient() : throw Unsupported(m, "speech to text"));
        TextToSpeech = Slot<ITextToSpeechClient>("Text to speech", "Reading text aloud", o.TextToSpeech,
            async (progress, ct) => await LocalTextToSpeechClient.LoadAsync(o.TextToSpeech.Model, o.TextToSpeech.Voice ?? "af_heart", progress, ct).ConfigureAwait(false),
            m => m.Provider == ModelProvider.OpenAI ? OpenAI(m).GetAudioClient(m.Model).AsITextToSpeechClient() : throw Unsupported(m, "text to speech"));
        VoiceActivity = Slot<IVoiceActivityDetector>("Voice activity", "Finding speech in audio", o.VoiceActivity,
            async (progress, ct) => await SileroVoiceActivityDetector.LoadAsync(o.VoiceActivity.Model, progress, ct).ConfigureAwait(false),
            m => throw Unsupported(m, "voice activity detection"));
    }

    public LocalAIOptions Options { get; }

    public ModelSlot<IChatClient> Chat { get; }
    public ModelSlot<IChatClient> Vision { get; }
    public ModelSlot<IEmbeddingGenerator<string, Embedding<float>>> Embeddings { get; }
    public ModelSlot<IReranker> Reranker { get; }
    public ModelSlot<ISpeechToTextClient> SpeechToText { get; }
    public ModelSlot<ITextToSpeechClient> TextToSpeech { get; }
    public ModelSlot<IVoiceActivityDetector> VoiceActivity { get; }

    public IReadOnlyList<IModelSlot> All => [Chat, Vision, Embeddings, Reranker, SpeechToText, TextToSpeech, VoiceActivity];

    private ModelSlot<IChatClient> ChatSlot(string name, string purpose, ChatModelOptions m) =>
        Slot<IChatClient>(name, purpose, m,
            async (progress, ct) => await LocalChatClient.LoadAsync(m.Model, new LocalChatModelOptions
            {
                UseGpu = Options.UseGpu,
                ContextSize = m.ContextSize,
                ProjectionSource = string.IsNullOrWhiteSpace(m.Projection) ? null : m.Projection,
                MaxConcurrentRequests = m.MaxConcurrentRequests,
            }, progress, ct).ConfigureAwait(false),
            hosted => hosted.Provider switch
            {
                ModelProvider.OpenAI => OpenAI(hosted).GetChatClient(hosted.Model).AsIChatClient(),
                ModelProvider.Ollama => new OllamaApiClient(OllamaUri(hosted), hosted.Model),
                _ => throw Unsupported(hosted, "chat"),
            });

    private static ModelSlot<T> Slot<T>(
        string name,
        string purpose,
        ModelOptions m,
        Func<IProgress<DownloadProgress>, CancellationToken, Task<T>> local,
        Func<ModelOptions, T> hosted) where T : class, IDisposable
    {
        if (!m.IsConfigured)
            return new ModelSlot<T>(name, purpose, "", m.Provider == ModelProvider.Local, null);
        if (m.Provider == ModelProvider.Local)
            return new ModelSlot<T>(name, purpose, m.Describe(), true, local);
        return new ModelSlot<T>(name, purpose, m.Describe(), false, (_, _) => Task.FromResult(hosted(m)));
    }

    private static OpenAIClient OpenAI(ModelOptions m)
    {
        var key = m.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            // OpenAI-compatible local servers usually accept any key.
            key = m.Endpoint is not null ? "unused" : throw new InvalidOperationException($"Set an API key for {m.Describe()} (ApiKey, or the OPENAI_API_KEY environment variable).");
        }
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(m.Endpoint))
            options.Endpoint = new Uri(m.Endpoint);
        return new OpenAIClient(new ApiKeyCredential(key), options);
    }

    private static Uri OllamaUri(ModelOptions m) => new(string.IsNullOrWhiteSpace(m.Endpoint) ? "http://localhost:11434" : m.Endpoint);

    private static NotSupportedException Unsupported(ModelOptions m, string capability) =>
        new($"{m.Provider} doesn't provide {capability} here. Use a local model for it.");

    public void Dispose()
    {
        foreach (var slot in All)
            (slot as IDisposable)?.Dispose();
    }
}
