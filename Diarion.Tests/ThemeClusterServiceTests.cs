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
    private readonly ThemeClusterService _service;

    public ThemeClusterServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);
        _service = new ThemeClusterService(_store, _embedder);
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
    public async Task Cluster_NoEncoder_ReturnsNothing()
    {
        var service = new ThemeClusterService(_store, new NullTextEmbedder());

        (await service.ClusterAsync(Start, End)).Should().BeEmpty();
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
        // No random initialisation anywhere, which is the reason k-means was not used.
        Indexed(3, "робота", [1f, 0f]);
        Indexed(4, "робота знову", [1f, 0.05f]);
        Indexed(10, "спорт", [0f, 1f]);
        Indexed(11, "спорт знову", [0.05f, 1f]);

        var first = await _service.ClusterAsync(Start, End);
        var second = await _service.ClusterAsync(Start, End);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
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
    }
}
