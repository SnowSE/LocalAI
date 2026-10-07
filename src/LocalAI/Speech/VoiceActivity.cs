using LocalAI.Audio;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LocalAI;

/// <summary>A stretch of audio where someone is speaking.</summary>
public readonly record struct SpeechSegment(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Tuning for <see cref="IVoiceActivityDetector"/>.</summary>
public sealed record VoiceActivityOptions
{
    /// <summary>Speech probability above which a window counts as speech. Defaults to 0.5.</summary>
    public float Threshold { get; init; } = 0.5f;

    /// <summary>Shorter bursts are ignored. Defaults to 250 ms.</summary>
    public TimeSpan MinSpeech { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Pauses shorter than this don't split speech. Defaults to 100 ms.</summary>
    public TimeSpan MinSilence { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Padding added to each side of a segment. Defaults to 30 ms.</summary>
    public TimeSpan Padding { get; init; } = TimeSpan.FromMilliseconds(30);
}

/// <summary>
/// Finds where people are speaking in audio, so a voice app knows when to start and stop
/// listening. Microsoft.Extensions.AI has no abstraction for this; this fills that gap.
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    Task<IReadOnlyList<SpeechSegment>> DetectAsync(AudioData audio, VoiceActivityOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>The Silero VAD model on ONNX Runtime. Small (about 2 MB) and fast on a CPU.</summary>
public sealed class SileroVoiceActivityDetector : IVoiceActivityDetector
{
    /// <summary>Silero VAD v5 from the onnx-community repo.</summary>
    public const string DefaultSource = "hf://onnx-community/silero-vad/onnx/model.onnx";

    private const int Rate = 16000;
    private const int Window = 512;
    private const int ContextLength = 64;
    private readonly InferenceSession _session;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SileroVoiceActivityDetector(InferenceSession session) => _session = session;

    public static async Task<SileroVoiceActivityDetector> LoadAsync(
        string? source = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await ModelSource.ResolveAsync(string.IsNullOrWhiteSpace(source) ? DefaultSource : source, progress, cancellationToken).ConfigureAwait(false);
        var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        return new SileroVoiceActivityDetector(new InferenceSession(path, options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpeechSegment>> DetectAsync(AudioData audio, VoiceActivityOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new VoiceActivityOptions();
        var samples = audio.Resample(Rate).Samples;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var probabilities = await Task.Run(() => Probabilities(samples, cancellationToken), cancellationToken).ConfigureAwait(false);
            return Segments(probabilities, samples.Length, options);
        }
        finally
        {
            _gate.Release();
        }
    }

    private float[] Probabilities(float[] samples, CancellationToken cancellationToken)
    {
        var windows = (samples.Length + Window - 1) / Window;
        var result = new float[windows];
        var state = new DenseTensor<float>(new float[2 * 128], [2, 1, 128]);
        var sr = new DenseTensor<long>(new[] { (long)Rate }, ReadOnlySpan<int>.Empty);
        var input = new float[ContextLength + Window];
        var tensor = new DenseTensor<float>(input, [1, ContextLength + Window]);

        for (var w = 0; w < windows; w++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Each window is fed with the last 64 samples before it, as Silero's own wrapper does.
            Array.Copy(input, Window, input, 0, ContextLength);
            Array.Clear(input, ContextLength, Window);
            var start = w * Window;
            Array.Copy(samples, start, input, ContextLength, Math.Min(Window, samples.Length - start));

            using var outputs = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", tensor),
                NamedOnnxValue.CreateFromTensor("state", state),
                NamedOnnxValue.CreateFromTensor("sr", sr),
            ]);
            foreach (var output in outputs)
            {
                if (output.Name == "output")
                    result[w] = output.AsTensor<float>().GetValue(0);
                else if (output.Name == "stateN")
                    state = new DenseTensor<float>(output.AsTensor<float>().ToArray(), [2, 1, 128]);
            }
        }
        return result;
    }

    /// <summary>Silero's <c>get_speech_timestamps</c>: hysteresis on the probabilities, then length and padding rules.</summary>
    private static List<SpeechSegment> Segments(float[] probabilities, int totalSamples, VoiceActivityOptions options)
    {
        var negative = Math.Max(options.Threshold - 0.15f, 0.01f);
        var minSpeech = (int)(options.MinSpeech.TotalSeconds * Rate);
        var minSilence = (int)(options.MinSilence.TotalSeconds * Rate);
        var pad = (int)(options.Padding.TotalSeconds * Rate);

        var spans = new List<(int Start, int End)>();
        var triggered = false;
        int start = 0, silenceStart = -1;
        for (var w = 0; w < probabilities.Length; w++)
        {
            var at = w * Window;
            var p = probabilities[w];
            if (p >= options.Threshold)
            {
                silenceStart = -1;
                if (!triggered)
                {
                    triggered = true;
                    start = at;
                }
            }
            else if (p < negative && triggered)
            {
                if (silenceStart < 0)
                    silenceStart = at;
                if (at - silenceStart >= minSilence)
                {
                    if (silenceStart - start >= minSpeech)
                        spans.Add((start, silenceStart));
                    triggered = false;
                    silenceStart = -1;
                }
            }
        }
        if (triggered && totalSamples - start >= minSpeech)
            spans.Add((start, totalSamples));

        var segments = new List<SpeechSegment>(spans.Count);
        for (var i = 0; i < spans.Count; i++)
        {
            var s = Math.Max(0, spans[i].Start - pad);
            var e = Math.Min(totalSamples, spans[i].End + pad);
            // Padding must not make neighbours overlap; split the gap between them instead.
            if (i > 0 && s < (int)(segments[^1].End.TotalSeconds * Rate))
            {
                var middle = (spans[i - 1].End + spans[i].Start) / 2;
                segments[^1] = segments[^1] with { End = TimeSpan.FromSeconds((double)middle / Rate) };
                s = middle;
            }
            segments.Add(new SpeechSegment(TimeSpan.FromSeconds((double)s / Rate), TimeSpan.FromSeconds((double)e / Rate)));
        }
        return segments;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.Dispose();
        _gate.Dispose();
    }
}
