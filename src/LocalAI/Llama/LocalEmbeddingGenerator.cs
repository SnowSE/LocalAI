using System.Numerics.Tensors;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LocalAI.Llama;
using Microsoft.Extensions.AI;

namespace LocalAI;

/// <summary>
/// A GGUF embedding model (such as bge or nomic-embed) loaded into this process, used through
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>. Vectors are L2-normalized, so cosine
/// similarity is a dot product.
/// </summary>
public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly EmbeddingGeneratorMetadata _metadata;
    private readonly string _name;

    private LocalEmbeddingGenerator(LLamaWeights weights, LLamaContext context, string name)
    {
        _weights = weights;
        _context = context;
        _name = name;
        _metadata = new EmbeddingGeneratorMetadata("LocalAI", null, name, context.EmbeddingSize);
    }

    /// <summary>Length of each embedding vector.</summary>
    public int Dimensions => _context.EmbeddingSize;

    /// <summary>Load a GGUF embedding model from a path or an <c>hf://</c> source.</summary>
    public static async Task<LocalEmbeddingGenerator> LoadAsync(
        string source,
        bool useGpu = true,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = await ModelSource.ResolveAsync(source, progress, cancellationToken).ConfigureAwait(false);
        LlamaBackend.EnsureConfigured(useGpu);
        var @params = new ModelParams(path)
        {
            GpuLayerCount = useGpu ? -1 : 0,
            Embeddings = true,
            ContextSize = 0, // The model's own training context.
        };
        var weights = await LLamaWeights.LoadFromFileAsync(@params, cancellationToken).ConfigureAwait(false);
        try
        {
            // Embedding models process each input in one batch.
            @params.ContextSize = (uint)Math.Min(weights.ContextSize, 8192);
            @params.BatchSize = @params.ContextSize.Value;
            @params.UBatchSize = @params.ContextSize.Value;
            var context = weights.CreateContext(@params);
            var name = weights.Metadata.GetValueOrDefault("general.name") ?? Path.GetFileNameWithoutExtension(path);
            return new LocalEmbeddingGenerator(weights, context, name);
        }
        catch
        {
            weights.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new GeneratedEmbeddings<Embedding<float>>();
        long tokens = 0;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (vector, count) = await EmbedAsync(value, cancellationToken).ConfigureAwait(false);
                tokens += count;
                results.Add(new Embedding<float>(vector) { ModelId = _name, CreatedAt = DateTimeOffset.UtcNow });
            }
        }
        finally
        {
            _gate.Release();
        }
        results.Usage = new UsageDetails { InputTokenCount = tokens, TotalTokenCount = tokens };
        return results;
    }

    private async Task<(float[] Vector, int Tokens)> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var tokens = _weights.Tokenize(text, add_bos: true, special: true, Encoding.UTF8);
        if (_weights.Vocab.ShouldAddEOS && _weights.Vocab.EOS is { } eos && (tokens.Length == 0 || tokens[^1] != eos))
            tokens = [.. tokens, eos];
        var limit = (int)_context.BatchSize;
        if (tokens.Length > limit)
            tokens = tokens[..limit]; // Embedding models are trained on bounded inputs; truncate like they do.

        var handle = _context.NativeHandle;
        handle.MemoryClear();
        var batch = new LLamaBatch();
        batch.AddRange(tokens, 0, LLamaSeqId.Zero, logitsLast: true);
        if (handle.ModelHandle.HasEncoder && !handle.ModelHandle.HasDecoder)
        {
            var result = await _context.EncodeAsync(batch, cancellationToken).ConfigureAwait(false);
            if (result != EncodeResult.Ok)
                throw new InvalidOperationException($"Embedding failed ({result}).");
        }
        else
        {
            var result = await _context.DecodeAsync(batch, cancellationToken).ConfigureAwait(false);
            if (result != DecodeResult.Ok)
                throw new InvalidOperationException($"Embedding failed ({result}).");
        }

        var vector = handle.GetEmbeddingsSeq(LLamaSeqId.Zero).ToArray();
        var norm = TensorPrimitives.Norm(vector);
        if (norm > 0)
            TensorPrimitives.Divide(vector, norm, vector);
        return (vector, tokens.Length);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return _metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
        _weights.Dispose();
        _gate.Dispose();
    }
}
