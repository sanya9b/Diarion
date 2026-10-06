using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class SemanticSearchServiceTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 7, 15);

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;
    private readonly StubEmbedder _embedder = new();
    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<INoteService> _notes = new();
    private readonly FakeAiAvailability _availability = new();
    private readonly SemanticSearchService _service;

    public SemanticSearchServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);

        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(new List<DiaryEntry>());
        _notes.Setup(n => n.SearchNotesAsync(It.IsAny<string>())).ReturnsAsync(new List<Note>());

        _service = new SemanticSearchService(_store, _embedder, _diary.Object, _notes.Object, _availability);
    }

    public void Dispose() => _dbContext.Dispose();

    private void Indexed(string sourceId, string text, float[] vector, string kind = EmbeddingSourceKind.Diary, int ordinal = 0)
    {
        EmbeddingMath.NormalizeInPlace(vector);
        _store.UpsertBatch([new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(kind, sourceId, ordinal),
            SourceKind = kind,
            SourceId = sourceId,
            Ordinal = ordinal,
            SourceDate = Day,
            Text = text,
            ContentHash = "h",
            ModelId = StubEmbedder.Id,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        }]);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsNothing()
    {
        (await _service.SearchAsync("   ")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_FindsAnEntryWithNoSharedWords()
    {
        // The whole point of the feature: the lexical side cannot possibly match this.
        _embedder.Map("робота", [1f, 0f]);
        Indexed("e1", "засидівся в офісі до ночі", [1f, 0.05f]);
        Indexed("e2", "купив полуницю", [0f, 1f]);

        var hits = await _service.SearchAsync("робота");

        hits.Should().ContainSingle();
        hits[0].SourceId.Should().Be("e1");
    }

    [Fact]
    public async Task Search_DropsMatchesBelowTheCalibratedFloor()
    {
        // 0.28 comes from measurement: a true single-word match scored 0.306 and unrelated text
        // 0.259, so the floor sits between them.
        _embedder.Map("робота", [1f, 0f]);
        Indexed("weak", "щось геть інше", [0.2f, 1f]);

        (await _service.SearchAsync("робота")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_SeveralChunksOfOneEntry_CollapseToOneResult()
    {
        // Otherwise one long entry fills the first screen.
        _embedder.Map("робота", [1f, 0f]);
        Indexed("e1", "перший фрагмент", [1f, 0.1f]);
        Indexed("e1", "другий фрагмент", [1f, 0.2f], ordinal: 1);

        var hits = await _service.SearchAsync("робота");

        hits.Should().ContainSingle().Which.SourceId.Should().Be("e1");
    }

    [Fact]
    public async Task Search_WithoutAnEncoder_StillMatchesWords()
    {
        // Degradation, not failure: with no model installed the app is a keyword search, not broken.
        _availability.CanEmbed = false;
        _diary.Setup(d => d.GetAllEntriesAsync())
            .ReturnsAsync([new DiaryEntry { Id = Guid.NewGuid(), Date = Day, Gratitude = "за каву вранці" }]);

        var hits = await _service.SearchAsync("каву");

        hits.Should().ContainSingle();
        (await _service.IsSemanticAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Search_AiSwitchedOff_LosesTheSemanticHalfAndKeepsTheLexicalOne()
    {
        // Note search by substring existed before any of this and is not part of the AI module.
        // Switching AI off has to narrow the results, not break the screen — so the meaning match
        // disappears and the word match stays.
        _embedder.Map("робота", [1f, 0f]);
        Indexed("e1", "засидівся в офісі до ночі", [1f, 0.05f]);
        _diary.Setup(d => d.GetAllEntriesAsync())
            .ReturnsAsync([new DiaryEntry { Id = Guid.NewGuid(), Date = Day, Gratitude = "робота була важка" }]);

        _availability.CanEmbed = false;
        var hits = await _service.SearchAsync("робота");

        hits.Should().ContainSingle("the meaning match is gone and the word match remains");
        hits[0].SourceKind.Should().Be(EmbeddingSourceKind.Diary);
        hits[0].Snippet.Should().Contain("робота була важка");
    }

    [Fact]
    public async Task Search_LexicalRequiresEveryTerm()
    {
        _diary.Setup(d => d.GetAllEntriesAsync())
            .ReturnsAsync([new DiaryEntry { Id = Guid.NewGuid(), Date = Day, Gratitude = "за каву вранці" }]);

        (await _service.SearchAsync("каву ввечері")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_LexicalIgnoresEntriesTheUserNeverWroteIn()
    {
        _diary.Setup(d => d.GetAllEntriesAsync())
            .ReturnsAsync([new DiaryEntry { Id = Guid.NewGuid(), Date = Day, CycleDay = "каву" }]);

        (await _service.SearchAsync("каву")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_AnEntryFoundBothWays_OutranksOneFoundOnlyOnce()
    {
        // What fusion is for: agreement between two independent rankers is the strongest signal.
        var bothId = Guid.NewGuid();
        _embedder.Map("кава", [1f, 0f]);
        Indexed(bothId.ToString(), "ранкова кава", [1f, 0f]);
        Indexed("semantic-only", "щось близьке", [1f, 0.3f], ordinal: 0);
        _diary.Setup(d => d.GetAllEntriesAsync())
            .ReturnsAsync([new DiaryEntry { Id = bothId, Date = Day, Gratitude = "ранкова кава" }]);

        var hits = await _service.SearchAsync("кава");

        hits[0].SourceId.Should().Be(bothId.ToString());
    }

    [Fact]
    public async Task Search_Scope_CanExcludeNotes()
    {
        _embedder.Map("ідея", [1f, 0f]);
        Indexed("n1", "нотатка", [1f, 0f], kind: EmbeddingSourceKind.Note);
        Indexed("e1", "запис", [1f, 0f]);

        var hits = await _service.SearchAsync("ідея", SearchScope.Diary);

        hits.Should().ContainSingle().Which.SourceKind.Should().Be(EmbeddingSourceKind.Diary);
    }

    [Fact]
    public async Task Search_HonoursTheLimit()
    {
        _embedder.Map("щось", [1f, 0f]);
        for (var i = 0; i < 10; i++)
        {
            Indexed($"e{i}", $"фрагмент {i}", [1f, 0f]);
        }

        (await _service.SearchAsync("щось", limit: 3)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_LongSnippet_IsTruncatedWithAnEllipsis()
    {
        _embedder.Map("довге", [1f, 0f]);
        Indexed("e1", new string('я', 400), [1f, 0f]);

        var snippet = (await _service.SearchAsync("довге"))[0].Snippet;

        snippet.Should().EndWith("…");
        snippet.Length.Should().BeLessThan(200);
    }

    /// <summary>Returns whatever vector the test mapped for a phrase; unknown text is orthogonal.</summary>
    private sealed class StubEmbedder : ITextEmbedder
    {
        public const string Id = "stub";

        private readonly Dictionary<string, float[]> _vectors = new();

        public string ModelId => Id;

        public int Dimensions => 2;

        public bool IsAvailable => true;

        public void Map(string text, float[] vector)
        {
            EmbeddingMath.NormalizeInPlace(vector);
            _vectors[text] = vector;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(_vectors.TryGetValue(text, out var v) ? v : [0f, 1f]);

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => EmbedAsync(t, cancellationToken).Result).ToList());

        public void Unload()
        {
        }
    }
}
