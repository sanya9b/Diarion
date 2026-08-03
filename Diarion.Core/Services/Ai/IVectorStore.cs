using System.Collections.Generic;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>Which sources a similarity search should consider.</summary>
[Flags]
public enum SearchScope
{
    Diary = 1,
    Notes = 2,
    All = Diary | Notes,
}

/// <summary>
/// Persistence and similarity search over embedded chunks. Synchronous by design: LiteDB is, and
/// the callers already wrap their work in <c>Task.Run</c> the way every other service here does.
/// </summary>
public interface IVectorStore
{
    /// <summary>Inserts or replaces chunks. Ids are deterministic, so re-indexing overwrites.</summary>
    void UpsertBatch(IEnumerable<EmbeddingChunk> chunks);

    /// <summary>Removes every chunk belonging to one source document.</summary>
    int DeleteBySource(string sourceKind, string sourceId);

    /// <summary>Removes every chunk not produced by <paramref name="modelId"/>.</summary>
    int DeleteForeignModels(string modelId);

    /// <summary>Empties the collection. Used when AI is switched off.</summary>
    void Clear();

    /// <summary>
    /// Content hashes already stored for the given model, keyed by <c>kind|sourceId</c>. This is
    /// how the indexer derives its work queue — there is no cursor to lose and no timestamp to trust.
    /// </summary>
    IReadOnlyDictionary<string, string> GetIndexedHashes(string modelId);

    /// <summary>Number of stored chunks for the given model.</summary>
    int CountForModel(string modelId);

    /// <summary>
    /// Every chunk filed under a date in [<paramref name="start"/>, <paramref name="end"/>], both
    /// ends inclusive. Used by the digest, which reasons about a period rather than a query.
    /// </summary>
    IReadOnlyList<EmbeddingChunk> GetByDateRange(
        string modelId,
        DateTime start,
        DateTime end,
        SearchScope scope = SearchScope.All);

    /// <summary>
    /// Top <paramref name="limit"/> chunks by cosine similarity against an already-normalized query
    /// vector. Brute force over the whole collection: at diary scale this is a few milliseconds,
    /// and an ANN index would cost a native dependency plus an invalidation problem to save them.
    /// </summary>
    /// <param name="minScore">
    /// Unfiltered by default. Deciding that a match is too weak to show — or too weak to answer
    /// from — is the caller's policy, not the store's.
    /// </param>
    IReadOnlyList<ScoredChunk> Search(
        float[] queryVector,
        string modelId,
        int limit,
        SearchScope scope = SearchScope.All,
        float minScore = float.NegativeInfinity);
}

/// <summary>A stored chunk together with its similarity to the query.</summary>
public sealed record ScoredChunk(EmbeddingChunk Chunk, float Score);
