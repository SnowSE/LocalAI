namespace LocalAI;

/// <summary>One document's relevance to a query.</summary>
/// <param name="Index">Position of the document in the list that was ranked.</param>
/// <param name="Document">The document's text.</param>
/// <param name="Score">Relevance; higher is more relevant. Between 0 and 1 for local cross-encoders.</param>
public sealed record RerankResult(int Index, string Document, float Score);

/// <summary>
/// Scores documents by how well they answer a query, reading each one together with the query.
/// Slower than comparing embeddings, but more accurate, so it is used to reorder the best few
/// matches from an embedding search. Microsoft.Extensions.AI has no reranking abstraction yet;
/// this fills that gap.
/// </summary>
public interface IReranker : IDisposable
{
    /// <summary>Score every document against <paramref name="query"/>, best first.</summary>
    Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IEnumerable<string> documents, CancellationToken cancellationToken = default);
}
