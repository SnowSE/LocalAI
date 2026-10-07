using System.Runtime.CompilerServices;
using LocalAI.Audio;
using Microsoft.Extensions.AI;
using Whisper.net;

namespace LocalAI;

/// <summary>
/// A Whisper model (whisper.cpp's GGML format) loaded into this process, used through
/// <see cref="ISpeechToTextClient"/>. Accepts WAV or MP3 at any sample rate.
/// </summary>
/// <remarks>
/// <see cref="SpeechToTextOptions.SpeechLanguage"/> sets the spoken language (detected when
/// unset). Setting <see cref="SpeechToTextOptions.TextLanguage"/> to English for speech in another
/// language translates it, which is the one translation Whisper can do.
/// </remarks>
public sealed class LocalSpeechToTextClient : ISpeechToTextClient
{
    private const int WhisperSampleRate = 16000;
    private readonly WhisperFactory _factory;
    private readonly SpeechToTextClientMetadata _metadata;
    private readonly string _name;

    private LocalSpeechToTextClient(WhisperFactory factory, string name)
    {
        _factory = factory;
        _name = name;
        _metadata = new SpeechToTextClientMetadata("LocalAI", null, name);
    }

    /// <summary>
    /// Load a Whisper GGML model (such as <c>hf://ggerganov/whisper.cpp/ggml-base.bin</c>) from a path
    /// or <c>hf://</c> source.
    /// </summary>
    public static async Task<LocalSpeechToTextClient> LoadAsync(
        string source,
        bool useGpu = true,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await ModelSource.ResolveAsync(source, progress, cancellationToken).ConfigureAwait(false);
        var factory = await Task.Run(() => WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = useGpu }), cancellationToken).ConfigureAwait(false);
        return new LocalSpeechToTextClient(factory, Path.GetFileNameWithoutExtension(path));
    }

    /// <inheritdoc />
    public async Task<SpeechToTextResponse> GetTextAsync(Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default) =>
        await GetStreamingTextAsync(audioSpeechStream, options, cancellationToken).ToSpeechToTextResponseAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);
        var audio = (await AudioData.DecodeAsync(audioSpeechStream, cancellationToken).ConfigureAwait(false)).Resample(WhisperSampleRate);
        var responseId = Guid.NewGuid().ToString("N");

        var builder = _factory.CreateBuilder();
        builder = options?.SpeechLanguage is { Length: > 0 } language ? builder.WithLanguage(language) : builder.WithLanguageDetection();
        if (options?.TextLanguage is { } target && target.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
            options.SpeechLanguage is { } spoken && !spoken.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            builder = builder.WithTranslate();

        await using var processor = builder.Build();
        yield return new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionOpen, ResponseId = responseId, ModelId = _name };
        var first = true;
        await foreach (var segment in processor.ProcessAsync(audio.Samples, cancellationToken).ConfigureAwait(false))
        {
            // Whisper starts each segment with a space; drop it from the first so the text starts cleanly.
            var text = first ? segment.Text.TrimStart() : segment.Text;
            first = false;
            yield return new SpeechToTextResponseUpdate(text)
            {
                Kind = SpeechToTextResponseUpdateKind.TextUpdated,
                ResponseId = responseId,
                ModelId = _name,
                StartTime = segment.Start,
                EndTime = segment.End,
            };
        }
        yield return new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionClose, ResponseId = responseId, ModelId = _name };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(SpeechToTextClientMetadata))
            return _metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose() => _factory.Dispose();
}
