using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using LocalAI.Audio;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;

namespace LocalAI;

/// <summary>
/// The Kokoro text-to-speech model, running on ONNX Runtime in this process, used through
/// <see cref="ITextToSpeechClient"/>. Returns 24 kHz mono WAV. Any length of text works: it is
/// spoken a sentence or so at a time.
/// </summary>
/// <remarks>
/// <see cref="TextToSpeechOptions.VoiceId"/> picks the voice (default <see cref="DefaultVoice"/>);
/// the voice's name sets its language, so <c>ff_siwis</c> speaks French. See
/// <see cref="KokoroVoiceCatalog"/> for them all. <see cref="TextToSpeechOptions.Speed"/> works too.
/// </remarks>
public sealed class LocalTextToSpeechClient : ITextToSpeechClient
{
    /// <summary>Kokoro's output rate.</summary>
    public const int SampleRate = 24000;

    /// <summary>Kokoro v1.0 in full precision, from the KokoroSharp releases.</summary>
    public const string DefaultSource = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx";

    private readonly KokoroWavSynthesizer _synthesizer;
    private readonly TextToSpeechClientMetadata _metadata = new("LocalAI", null, "kokoro-82m");

    private LocalTextToSpeechClient(KokoroWavSynthesizer synthesizer, string defaultVoice)
    {
        _synthesizer = synthesizer;
        DefaultVoice = defaultVoice;
    }

    /// <summary>The voice used when a request doesn't name one.</summary>
    public string DefaultVoice { get; }

    /// <summary>Load Kokoro from a path, an <c>https://</c> URL or an <c>hf://</c> source.</summary>
    public static async Task<LocalTextToSpeechClient> LoadAsync(
        string? source = null,
        string defaultVoice = "af_heart",
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await ModelSource.ResolveAsync(string.IsNullOrWhiteSpace(source) ? DefaultSource : source, progress, cancellationToken).ConfigureAwait(false);
        var synthesizer = await Task.Run(() =>
        {
            var session = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2) };
            return new KokoroWavSynthesizer(path, session);
        }, cancellationToken).ConfigureAwait(false);
        // Fail now, not on the first request, if the voice doesn't exist.
        _ = Voice(defaultVoice);
        return new LocalTextToSpeechClient(synthesizer, defaultVoice);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default)
    {
        var samples = new List<float>();
        await foreach (var chunk in SynthesizeAsync(text, options, cancellationToken).ConfigureAwait(false))
            samples.AddRange(chunk);
        var wav = new AudioData([.. samples], SampleRate).ToWav();
        return new TextToSpeechResponse([new DataContent(wav, "audio/wav")]) { ModelId = _metadata.DefaultModelId };
    }

    /// <inheritdoc />
    /// <remarks>Each update is a complete WAV file for the next stretch of speech, so it can be played as soon as it arrives.</remarks>
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var responseId = Guid.NewGuid().ToString("N");
        await foreach (var chunk in SynthesizeAsync(text, options, cancellationToken).ConfigureAwait(false))
        {
            yield return new TextToSpeechResponseUpdate([new DataContent(new AudioData(chunk, SampleRate).ToWav(), "audio/wav")])
            {
                ResponseId = responseId,
                ModelId = _metadata.DefaultModelId,
            };
        }
    }

    private async IAsyncEnumerable<float[]> SynthesizeAsync(string text, TextToSpeechOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var voice = Voice(options?.VoiceId ?? DefaultVoice);
        var config = new KokoroTTSPipelineConfig(new DefaultSegmentationConfig { MaxFirstSegmentLength = 510 })
        {
            Speed = options?.Speed ?? 1f,
        };

        var tokens = Tokenizer.Tokenize(text.Trim(), voice.GetLangCode(), config.PreprocessText);
        var segments = config.SegmentationFunc(tokens);
        if (segments.Count == 0)
            yield break;

        // KokoroSharp runs each segment on its worker thread; hand the audio over through a channel.
        var channel = Channel.CreateUnbounded<float[]>();
        var job = _synthesizer.EnqueueJob(KokoroJob.Create(segments, voice, config.Speed, null));
        foreach (var step in job.Steps)
        {
            step.OnStepComplete = samples =>
            {
                var trimmed = KokoroPlayback.PostProcessSamples(samples, out _);
                // A segment that ends a sentence gets the natural pause after it.
                if (step.Tokens.Length > 0 && Tokenizer.PunctuationTokens.Contains(step.Tokens[^1]))
                {
                    var pause = (int)(config.SecondsOfPauseBetweenProperSegments[Tokenizer.TokenToChar[step.Tokens[^1]]] * SampleRate);
                    Array.Resize(ref trimmed, trimmed.Length + pause);
                }
                channel.Writer.TryWrite(trimmed);
                if (step == job.Steps[^1])
                    channel.Writer.TryComplete();
            };
        }

        await foreach (var samples in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return samples;
    }

    private static KokoroVoice Voice(string id)
    {
        try
        {
            return KokoroVoiceManager.GetVoice(id);
        }
        catch (InvalidOperationException)
        {
            throw new ArgumentException($"Kokoro has no voice named '{id}'. Pick one from KokoroVoiceCatalog.All.", nameof(id));
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(TextToSpeechClientMetadata))
            return _metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose() => _synthesizer.Dispose();
}
