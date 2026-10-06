using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class LiteDbVectorStoreTests : IDisposable
{
    private const string Model = "minilm-v1";
    private static readonly DateTime Day = new(2026, 7, 15);

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;

    public LiteDbVectorStoreTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private static EmbeddingChunk Chunk(
        string sourceId,
        float[] vector,
        int ordinal = 0,
        string kind = EmbeddingSourceKind.Diary,
        string hash = "h1",
        string model = Model,
        string text = "текст",
        DateTime? date = null)
    {
        EmbeddingMath.NormalizeInPlace(vector);
        return new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(kind, sourceId, ordinal),
            SourceKind = kind,
            SourceId = sourceId,
            Ordinal = ordinal,
            SourceDate = date ?? Day,
            Text = text,
            ContentHash = hash,
            ModelId = model,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        };
    }

    [Fact]
    public void UpsertBatch_RoundTripsTheVector()
    {
        _store.UpsertBatch(new[] { Chunk("a", new[] { 3f, 4f }) });

        var hit = _store.Search(Normalized(3f, 4f), Model, limit: 1).Single();

        hit.Chunk.SourceId.Should().Be("a");
        hit.Score.Should().BeApproximately(1f, 1e-5f);
        EmbeddingMath.FromBytes(hit.Chunk.Vector).Should().Equal(0.6f, 0.8f);
    }

    [Fact]
    public void UpsertBatch_SameSourceAndOrdinal_Replaces_RatherThanAccumulating()
    {
        // Deterministic ids are the whole reason re-indexing is safe to interrupt and retry.
        _store.UpsertBatch(new[] { Chunk("a", new[] { 1f, 0f }, text: "старий") });
        _store.UpsertBatch(new[] { Chunk("a", new[] { 1f, 0f }, text: "новий") });

        _store.CountForModel(Model).Should().Be(1);
        _store.Search(Normalized(1f, 0f), Model, limit: 5).Single().Chunk.Text.Should().Be("новий");
    }

    [Fact]
    public void UpsertBatch_EmptyBatch_IsANoOp()
    {
        _store.UpsertBatch(Array.Empty<EmbeddingChunk>());

        _store.CountForModel(Model).Should().Be(0);
    }

    [Fact]
    public void Search_RanksByCosineSimilarity()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("near", new[] { 1f, 0.1f }),
            Chunk("far", new[] { -1f, 0f }),
            Chunk("middle", new[] { 1f, 1f }),
        });

        var ids = _store.Search(Normalized(1f, 0f), Model, limit: 3).Select(r => r.Chunk.SourceId);

        // "far" is a negative similarity and still comes back: filtering weak matches is the
        // caller's policy, not the store's.
        ids.Should().Equal("near", "middle", "far");
    }

    [Fact]
    public void Search_HonoursTheLimit()
    {
        _store.UpsertBatch(Enumerable.Range(0, 10).Select(i => Chunk($"s{i}", new[] { 1f, i })).ToList());

        _store.Search(Normalized(1f, 0f), Model, limit: 3).Should().HaveCount(3);
    }

    [Fact]
    public void Search_MinScore_DropsWeakMatches()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("near", new[] { 1f, 0f }),
            Chunk("opposite", new[] { -1f, 0f }),
        });

        var hits = _store.Search(Normalized(1f, 0f), Model, limit: 10, minScore: 0.5f);

        hits.Should().ContainSingle().Which.Chunk.SourceId.Should().Be("near");
    }

    [Fact]
    public void Search_IgnoresChunksFromAnotherModel()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("current", new[] { 1f, 0f }),
            Chunk("stale", new[] { 1f, 0f }, ordinal: 1, model: "other-model"),
        });

        _store.Search(Normalized(1f, 0f), Model, limit: 10)
            .Should().ContainSingle().Which.Chunk.SourceId.Should().Be("current");
    }

    [Fact]
    public void Search_IgnoresChunksOfADifferentWidth()
    {
        // A leftover from an earlier model that happened to reuse an id: comparing 3 dimensions to
        // 2 is meaningless, and skipping beats throwing in the middle of a user's search.
        _store.UpsertBatch(new[]
        {
            Chunk("good", new[] { 1f, 0f }),
            Chunk("wrongwidth", new[] { 1f, 0f, 0f }, ordinal: 1),
        });

        _store.Search(Normalized(1f, 0f), Model, limit: 10)
            .Should().ContainSingle().Which.Chunk.SourceId.Should().Be("good");
    }

    [Theory]
    [InlineData(SearchScope.Diary, "entry")]
    [InlineData(SearchScope.Notes, "note")]
    public void Search_Scope_FiltersBySourceKind(SearchScope scope, string expected)
    {
        _store.UpsertBatch(new[]
        {
            Chunk("entry", new[] { 1f, 0f }),
            Chunk("note", new[] { 1f, 0f }, kind: EmbeddingSourceKind.Note),
        });

        _store.Search(Normalized(1f, 0f), Model, limit: 10, scope)
            .Should().ContainSingle().Which.Chunk.SourceId.Should().Be(expected);
    }

    [Fact]
    public void Search_EmptyStore_ReturnsNothing()
    {
        _store.Search(Normalized(1f, 0f), Model, limit: 5).Should().BeEmpty();
    }

    [Fact]
    public void DeleteBySource_RemovesEveryChunkOfThatDocumentOnly()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("a", new[] { 1f, 0f }, ordinal: 0),
            Chunk("a", new[] { 0f, 1f }, ordinal: 1),
            Chunk("b", new[] { 1f, 1f }),
        });

        _store.DeleteBySource(EmbeddingSourceKind.Diary, "a").Should().Be(2);
        _store.CountForModel(Model).Should().Be(1);
    }

    [Fact]
    public void DeleteForeignModels_KeepsOnlyTheActiveModel()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("a", new[] { 1f, 0f }),
            Chunk("b", new[] { 1f, 0f }, model: "old-model"),
        });

        _store.DeleteForeignModels(Model).Should().Be(1);
        _store.CountForModel(Model).Should().Be(1);
    }

    [Fact]
    public void Clear_EmptiesTheCollection()
    {
        _store.UpsertBatch(new[] { Chunk("a", new[] { 1f, 0f }), Chunk("b", new[] { 0f, 1f }) });

        _store.Clear();

        _store.CountForModel(Model).Should().Be(0);
    }

    [Fact]
    public void GetIndexedHashes_ReportsOneHashPerDocument()
    {
        _store.UpsertBatch(new[]
        {
            Chunk("a", new[] { 1f, 0f }, ordinal: 0, hash: "hash-a"),
            Chunk("a", new[] { 0f, 1f }, ordinal: 1, hash: "hash-a"),
            Chunk("n", new[] { 1f, 1f }, kind: EmbeddingSourceKind.Note, hash: "hash-n"),
        });

        var hashes = _store.GetIndexedHashes(Model);

        hashes.Should().HaveCount(2);
        hashes[LiteDbVectorStore.SourceKey(EmbeddingSourceKind.Diary, "a")].Should().Be("hash-a");
        hashes[LiteDbVectorStore.SourceKey(EmbeddingSourceKind.Note, "n")].Should().Be("hash-n");
    }

    [Fact]
    public void GetIndexedHashes_IgnoresOtherModels()
    {
        _store.UpsertBatch(new[] { Chunk("a", new[] { 1f, 0f }, model: "other-model") });

        _store.GetIndexedHashes(Model).Should().BeEmpty();
    }

    private static float[] Normalized(params float[] vector)
    {
        EmbeddingMath.NormalizeInPlace(vector);
        return vector;
    }
}
