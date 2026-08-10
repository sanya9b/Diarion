using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class ThemeClusterServiceTests : IDisposable
{
    private const string Model = "stub";
    private static readonly DateTime Start = new(2026, 6, 1);
    private static readonly DateTime End = new(2026, 6, 30);

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;
    private readonly StubEmbedder _embedder = new();
    private readonly FakeAiAvailability _availability = new();
    private readonly ThemeClusterService _service;

    public ThemeClusterServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);
        _service = new ThemeClusterService(_store, _embedder, _availability);
    }

    public void Dispose() => _dbContext.Dispose();

    private int _ordinal;

    /// <summary>Pads to clear the theme length floor, so tests exercise clustering and not the filter.</summary>
    private void Indexed(int day, string text, float[] vector, string kind = EmbeddingSourceKind.Diary)
    {
        text = text.Length >= 40 ? text : text.PadRight(45, '.');
        var date = new DateTime(2026, 6, day);
        EmbeddingMath.NormalizeInPlace(vector);
        var id = $"{kind}-{day}-{_ordinal}";
        _store.UpsertBatch([new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(kind, id, _ordinal++),
            SourceKind = kind,
            SourceId = id,
            SourceDate = date,
            Text = text,
            ContentHash = "h",
            ModelId = Model,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        }]);
    }

    [Fact]
    public async Task Cluster_AiUnavailable_ReturnsNothing()
    {
        _availability.CanEmbed = false;

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_AiSwitchedOff_TakesThemesOffTheDashboard()
    {
        // Themes are the most visible AI output in the app — they sit on the statistics screen
        // without being asked for. Leaving them there after the toggle went off would be the
        // clearest possible contradiction of it.
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(5, "знову про роботу", [1f, 0f]);
        (await _service.ClusterAsync(Start, End)).Should().NotBeEmpty("otherwise the test proves nothing");

        _availability.CanEmbed = false;

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_EmptyPeriod_ReturnsNothing()
    {
        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_SomethingWrittenOnce_IsNotATheme()
    {
        // An entry is not a theme. A theme has to recur.
        Indexed(3, "єдиний раз про це", [1f, 0f]);

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_SamePassageOnOneDayTwice_IsStillNotATheme()
    {
        // Counting passages instead of days would call one long evening a theme.
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(3, "робота і дедлайни знову", [1f, 0.02f]);

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_RecurringSubject_BecomesAThemeCountedInDays()
    {
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);
        Indexed(5, "робота допізна", [1f, 0.1f]);

        var themes = await _service.ClusterAsync(Start, End);

        themes.Should().ContainSingle();
        themes[0].DayCount.Should().Be(3);
        themes[0].FirstSeen.Should().Be(new DateTime(2026, 6, 3));
        themes[0].LastSeen.Should().Be(new DateTime(2026, 6, 5));
    }

    [Fact]
    public async Task Cluster_SeparatesUnrelatedSubjects()
    {
        Indexed(3, "робота", [1f, 0f]);
        Indexed(4, "робота знову", [1f, 0.05f]);
        Indexed(10, "спорт", [0f, 1f]);
        Indexed(11, "спорт знову", [0.05f, 1f]);

        var themes = await _service.ClusterAsync(Start, End);

        themes.Should().HaveCount(2);
        themes.Select(t => t.DayCount).Should().AllBeEquivalentTo(2);
    }

    [Fact]
    public async Task Cluster_RanksTheMostPresentThemeFirst()
    {
        Indexed(3, "робота", [1f, 0f]);
        Indexed(4, "робота", [1f, 0.02f]);
        Indexed(5, "робота", [1f, 0.04f]);
        Indexed(10, "спорт", [0f, 1f]);
        Indexed(11, "спорт", [0.02f, 1f]);

        var themes = await _service.ClusterAsync(Start, End);

        themes[0].DayCount.Should().Be(3);
        themes[0].Label.Should().Contain("робота");
    }

    [Fact]
    public async Task Cluster_HonoursTheThemeLimit()
    {
        for (var pair = 0; pair < 6; pair++)
        {
            var vector = new float[12];
            vector[pair * 2] = 1f;
            Indexed(1 + pair * 2, $"тема {pair}", (float[])vector.Clone());
            Indexed(2 + pair * 2, $"тема {pair} знову", (float[])vector.Clone());
        }

        (await _service.ClusterAsync(Start, End, maxThemes: 3)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Cluster_LabelIsTheFirstSentenceVerbatim()
    {
        Indexed(3, "Засидівся в офісі. Потім ще довго не міг заснути.", [1f, 0f]);
        Indexed(4, "Засидівся в офісі знову.", [1f, 0.02f]);

        var themes = await _service.ClusterAsync(Start, End);

        themes[0].Label.Should().Be("Засидівся в офісі");
    }

    [Fact]
    public async Task Cluster_IgnoresNotes()
    {
        Indexed(3, "нотатка", [1f, 0f], EmbeddingSourceKind.Note);
        Indexed(4, "нотатка знову", [1f, 0.02f], EmbeddingSourceKind.Note);

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_OneWordAnswers_AreNotThemes()
    {
        // The dashboard's top theme was once the word "Ні" over 41 days — the one-word intimate-life
        // field. Meaningless as a subject, and not something to headline a summary screen with.
        for (var day = 3; day <= 12; day++)
        {
            IndexedRaw(day, "Ні", [1f, 0f]);
        }

        (await _service.ClusterAsync(Start, End)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cluster_IsDeterministic()
    {
        // No random initialisation anywhere, which is the reason k-means was not used. Asked of a
        // second instance on purpose: the same one would answer from its memo and prove nothing.
        Indexed(3, "робота", [1f, 0f]);
        Indexed(4, "робота знову", [1f, 0.05f]);
        Indexed(10, "спорт", [0f, 1f]);
        Indexed(11, "спорт знову", [0.05f, 1f]);

        var first = await _service.ClusterAsync(Start, End);
        var second = await new ThemeClusterService(_store, _embedder, _availability).ClusterAsync(Start, End);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Cluster_DaysAreDistinctAndAscending()
    {
        // The correlation pass reads these days directly, and a series built from an unsorted or
        // duplicated list would silently count one day twice.
        Indexed(5, "робота допізна", [1f, 0f]);
        Indexed(3, "робота і дедлайни", [1f, 0.02f]);
        Indexed(3, "робота, ще раз про неї", [1f, 0.03f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);

        var theme = (await _service.ClusterAsync(Start, End))[0];

        theme.Days.Should().Equal(
            new DateTime(2026, 6, 3),
            new DateTime(2026, 6, 4),
            new DateTime(2026, 6, 5));
        theme.DayCount.Should().Be(theme.Days.Count);
        theme.FirstSeen.Should().Be(theme.Days[0]);
        theme.LastSeen.Should().Be(theme.Days[^1]);
    }

    [Fact]
    public async Task Summarise_IndexedDaysCoverEveryDayWritten_EvenWhenNothingWasThemed()
    {
        // The denominator for "the theme was absent". A day of one-word answers is still a day the
        // user wrote and this theme was not what they wrote about; a day with no entry at all is
        // not, and must stay out or absence gets invented.
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);
        IndexedRaw(7, "Ні", [0f, 1f]);

        var summary = await _service.SummariseAsync(Start, End);

        summary.Themes.Should().ContainSingle();
        summary.Themes[0].Days.Should().NotContain(new DateTime(2026, 6, 7));
        summary.IndexedDays.Should().Equal(
            new DateTime(2026, 6, 3),
            new DateTime(2026, 6, 4),
            new DateTime(2026, 6, 7));
    }

    [Fact]
    public async Task Summarise_NothingIndexed_HasNoDaysEither()
    {
        var summary = await _service.SummariseAsync(Start, End);

        summary.Themes.Should().BeEmpty();
        summary.IndexedDays.Should().BeEmpty();
    }

    [Fact]
    public async Task Summarise_SameWindowTwice_ClustersOnce()
    {
        // The statistics screen asks for this window twice in a row — the digest, then the mood
        // correlations. Re-reading the rows is cheap; re-clustering them is O(k·n²).
        var counting = new CountingVectorStore(_store);
        var service = new ThemeClusterService(counting, _embedder, _availability);
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);

        var first = await service.SummariseAsync(Start, End);
        var second = await service.SummariseAsync(Start, End);

        second.Should().BeSameAs(first);
        counting.RangeReads.Should().Be(2, "the rows are re-read; only the clustering is reused");
    }

    [Fact]
    public async Task Summarise_AfterTheDiaryChanges_ClustersAgain()
    {
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);
        var first = await _service.SummariseAsync(Start, End);

        Indexed(10, "зовсім інша тема, про спорт", [0f, 1f]);
        Indexed(11, "спорт знову, про біг зранку", [0.05f, 1f]);

        (await _service.SummariseAsync(Start, End)).Themes.Should().HaveCount(2)
            .And.NotBeSameAs(first.Themes);
    }

    [Fact]
    public async Task Summarise_ADifferentWindow_IsNotServedFromTheMemo()
    {
        Indexed(3, "робота і дедлайни", [1f, 0f]);
        Indexed(4, "знову дедлайни", [1f, 0.05f]);

        (await _service.SummariseAsync(Start, End)).Themes.Should().ContainSingle();
        (await _service.SummariseAsync(new DateTime(2026, 6, 20), End)).Themes.Should().BeEmpty();
    }

    /// <summary>Counts the reads the memo is supposed to keep cheap, and passes everything through.</summary>
    private sealed class CountingVectorStore(IVectorStore inner) : IVectorStore
    {
        public int RangeReads { get; private set; }

        public IReadOnlyList<EmbeddingChunk> GetByDateRange(
            string modelId, DateTime start, DateTime end, SearchScope scope = SearchScope.All)
        {
            RangeReads++;
            return inner.GetByDateRange(modelId, start, end, scope);
        }

        public void UpsertBatch(IEnumerable<EmbeddingChunk> chunks) => inner.UpsertBatch(chunks);

        public int DeleteBySource(string sourceKind, string sourceId) => inner.DeleteBySource(sourceKind, sourceId);

        public int DeleteForeignModels(string modelId) => inner.DeleteForeignModels(modelId);

        public void Clear() => inner.Clear();

        public IReadOnlyDictionary<string, string> GetIndexedHashes(string modelId) => inner.GetIndexedHashes(modelId);

        public int CountForModel(string modelId) => inner.CountForModel(modelId);

        public IReadOnlyList<ScoredChunk> Search(
            float[] queryVector, string modelId, int limit,
            SearchScope scope = SearchScope.All, float minScore = float.NegativeInfinity) =>
            inner.Search(queryVector, modelId, limit, scope, minScore);
    }

    private void IndexedRaw(int day, string text, float[] vector)
    {
        var date = new DateTime(2026, 6, day);
        EmbeddingMath.NormalizeInPlace(vector);
        var id = $"raw-{day}-{_ordinal}";
        _store.UpsertBatch([new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(EmbeddingSourceKind.Diary, id, _ordinal++),
            SourceKind = EmbeddingSourceKind.Diary,
            SourceId = id,
            SourceDate = date,
            Text = text,
            ContentHash = "h",
            ModelId = Model,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        }]);
    }

    private sealed class StubEmbedder : ITextEmbedder
    {
        public string ModelId => Model;

        public int Dimensions => 2;

        public bool IsAvailable => true;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<float[]>([1f, 0f]);

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());

        public void Unload()
        {
        }
    }
}
