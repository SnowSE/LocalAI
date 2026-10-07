using System.Runtime.CompilerServices;
using LocalAI.Audio;
using Microsoft.Extensions.AI;

namespace LocalAI.Hosting;

// Stand-ins registered with dependency injection. Each forwards to its slot's model, loading it on
// the first call, so services can take an IChatClient (and so on) in their constructors without
// waiting for a multi-gigabyte download. GetService(typeof(IModelSlot)) returns the slot itself.

internal sealed class LazyChatClient(ModelSlot<IChatClient> slot) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(slot) ? slot : slot.Value?.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        // The slot owns the model.
    }
}

internal sealed class LazyEmbeddingGenerator(ModelSlot<IEmbeddingGenerator<string, Embedding<float>>> slot) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(slot) ? slot : slot.Value?.GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }
}

internal sealed class LazySpeechToTextClient(ModelSlot<ISpeechToTextClient> slot) : ISpeechToTextClient
{
    public async Task<SpeechToTextResponse> GetTextAsync(Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).GetTextAsync(audioSpeechStream, options, cancellationToken).ConfigureAwait(false);

    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(Stream audioSpeechStream, SpeechToTextOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in client.GetStreamingTextAsync(audioSpeechStream, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(slot) ? slot : slot.Value?.GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }
}

internal sealed class LazyTextToSpeechClient(ModelSlot<ITextToSpeechClient> slot) : ITextToSpeechClient
{
    public async Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).GetAudioAsync(text, options, cancellationToken).ConfigureAwait(false);

    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(string text, TextToSpeechOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var update in client.GetStreamingAudioAsync(text, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(slot) ? slot : slot.Value?.GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }
}

internal sealed class LazyReranker(ModelSlot<IReranker> slot) : IReranker
{
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IEnumerable<string> documents, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).RerankAsync(query, documents, cancellationToken).ConfigureAwait(false);

    public void Dispose()
    {
    }
}

internal sealed class LazyVoiceActivityDetector(ModelSlot<IVoiceActivityDetector> slot) : IVoiceActivityDetector
{
    public async Task<IReadOnlyList<SpeechSegment>> DetectAsync(AudioData audio, VoiceActivityOptions? options = null, CancellationToken cancellationToken = default) =>
        await (await slot.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false)).DetectAsync(audio, options, cancellationToken).ConfigureAwait(false);

    public void Dispose()
    {
    }
}
