using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Ai;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services.Ai;

public class LiteDbVectorStore : IVectorStore
{
    private readonly IDatabaseContext _dbContext;

    public LiteDbVectorStore(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Re-fetched per call rather than cached: DatabaseContext.Reopen() disposes the underlying
    // LiteDatabase after a backup restore, and a held ILiteCollection would throw from then on.
    private ILiteCollection<EmbeddingChunk> Collection =>
        _dbContext.GetCollection<EmbeddingChunk>(DatabaseConstants.EmbeddingsCollection);

    public void UpsertBatch(IEnumerable<EmbeddingChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var batch = chunks as IList<EmbeddingChunk> ?? chunks.ToList();
        if (batch.Count == 0)
        {
            return;
        }

        Collection.Upsert(batch);
    }

    public int DeleteBySource(string sourceKind, string sourceId) =>
        Collection.DeleteMany(c => c.SourceKind == sourceKind && c.SourceId == sourceId);

    public int DeleteForeignModels(string modelId) =>
        Collection.DeleteMany(c => c.ModelId != modelId);

    public void Clear() => Collection.DeleteAll();

    public IReadOnlyDictionary<string, string> GetIndexedHashes(string modelId)
    {
        var hashes = new Dictionary<string, string>();

        foreach (var chunk in Collection.Find(c => c.ModelId == modelId))
        {
            // Every chunk of a document carries the same document-level hash, so the first one wins
            // and the rest are redundant confirmations.
            hashes.TryAdd(SourceKey(chunk.SourceKind, chunk.SourceId), chunk.ContentHash);
        }

        return hashes;
    }

    public int CountForModel(string modelId) => Collection.Count(c => c.ModelId == modelId);

    public IReadOnlyList<EmbeddingChunk> GetByDateRange(
        string modelId,
        DateTime start,
        DateTime end,
        SearchScope scope = SearchScope.All)
    {
        var from = start.Date;
        var to = end.Date;

        return Collection
            .Find(c => c.ModelId == modelId)
            .Where(c => IsInScope(c.SourceKind, scope))
            .Where(c => c.SourceDate.Date >= from && c.SourceDate.Date <= to)
            .ToList();
    }

    public IReadOnlyList<ScoredChunk> Search(
        float[] queryVector,
        string modelId,
        int limit,
        SearchScope scope = SearchScope.All,
        float minScore = float.NegativeInfinity)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var results = new List<ScoredChunk>();

        foreach (var chunk in Collection.Find(c => c.ModelId == modelId))
        {
            if (!IsInScope(chunk.SourceKind, scope))
            {
                continue;
            }

            // A stored vector of a different width is a leftover from an earlier model that shared
            // an id; scoring it would compare incomparable spaces, so skip rather than throw.
            if (chunk.Dim != queryVector.Length || chunk.Vector.Length != queryVector.Length * sizeof(float))
            {
                continue;
            }

            var score = EmbeddingMath.DotNormalized(queryVector, EmbeddingMath.FromBytes(chunk.Vector));
            if (score >= minScore)
            {
                results.Add(new ScoredChunk(chunk, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Chunk.SourceDate)
            .Take(limit)
            .ToList();
    }

    private static bool IsInScope(string sourceKind, SearchScope scope) => sourceKind switch
    {
        EmbeddingSourceKind.Diary => scope.HasFlag(SearchScope.Diary),
        EmbeddingSourceKind.Note => scope.HasFlag(SearchScope.Notes),
        _ => false,
    };

    public static string SourceKey(string sourceKind, string sourceId) => $"{sourceKind}|{sourceId}";
}
