using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LocalAI.Llama;

namespace LocalAI;

/// <summary>A GGUF cross-encoder (such as bge-reranker) loaded into this process.</summary>
public sealed class LocalReranker : IReranker
{
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _template;

    private LocalReranker(LLamaWeights weights, LLamaContext context)
    {
        _weights = weights;
        _context = context;
        // Newer rerankers ship their input format in the GGUF; older ones use BOS q EOS SEP d EOS.
        _template = weights.Metadata.GetValueOrDefault("tokenizer.chat_template.rerank");
    }

    /// <summary>Load a GGUF reranker from a path or an <c>hf://</c> source.</summary>
    public static async Task<LocalReranker> LoadAsync(
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
            PoolingType = LLamaPoolingType.Rank,
            ContextSize = 0,
        };
        var weights = await LLamaWeights.LoadFromFileAsync(@params, cancellationToken).ConfigureAwait(false);
        try
        {
            @params.ContextSize = (uint)Math.Min(weights.ContextSize, 8192);
            @params.BatchSize = @params.ContextSize.Value;
            @params.UBatchSize = @params.ContextSize.Value;
            return new LocalReranker(weights, weights.CreateContext(@params));
        }
        catch
        {
            weights.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IEnumerable<string> documents, CancellationToken cancellationToken = default)
    {
        var list = documents.ToList();
        var results = new List<RerankResult>(list.Count);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var score = await ScoreAsync(query, list[i], cancellationToken).ConfigureAwait(false);
                results.Add(new RerankResult(i, list[i], score));
            }
        }
        finally
        {
            _gate.Release();
        }
        return results.OrderByDescending(r => r.Score).ToList();
    }

    private async Task<float> ScoreAsync(string query, string document, CancellationToken cancellationToken)
    {
        var tokens = Tokens(query, document);
        var limit = (int)_context.BatchSize;
        if (tokens.Length > limit)
            tokens = tokens[..limit];

        var handle = _context.NativeHandle;
        handle.MemoryClear();
        var batch = new LLamaBatch();
        batch.AddRange(tokens, 0, LLamaSeqId.Zero, logitsLast: true);
        var result = await _context.DecodeAsync(batch, cancellationToken).ConfigureAwait(false);
        if (result != DecodeResult.Ok)
            throw new InvalidOperationException($"Reranking failed ({result}).");
        var logit = handle.GetEmbeddingsSeq(LLamaSeqId.Zero)[0];
        return 1f / (1f + MathF.Exp(-logit));
    }

    private LLamaToken[] Tokens(string query, string document)
    {
        if (_template is not null)
        {
            var text = _template.Replace("{query}", query, StringComparison.Ordinal).Replace("{document}", document, StringComparison.Ordinal);
            return _weights.Tokenize(text, add_bos: false, special: true, Encoding.UTF8);
        }

        var vocab = _weights.Vocab;
        var q = _weights.Tokenize(query, add_bos: false, special: false, Encoding.UTF8);
        var d = _weights.Tokenize(document, add_bos: false, special: false, Encoding.UTF8);
        var tokens = new List<LLamaToken>(q.Length + d.Length + 4);
        if (vocab.BOS is { } bos)
            tokens.Add(bos);
        tokens.AddRange(q);
        if (vocab.EOS is { } eos)
            tokens.Add(eos);
        if (vocab.SEP is { } sep)
            tokens.Add(sep);
        tokens.AddRange(d);
        if (vocab.EOS is { } end)
            tokens.Add(end);
        return [.. tokens];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
        _weights.Dispose();
        _gate.Dispose();
    }
}
