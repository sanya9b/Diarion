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

public class DigestServiceTests : IDisposable
{
    private const string Model = "stub";
    private static readonly DateTime Start = new(2026, 6, 1);
    private static readonly DateTime End = new(2026, 6, 30);

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;
    private readonly StubEmbedder _embedder = new();
    private readonly FakeAiAvailability _availability = new();
    private readonly DigestService _service;

    public DigestServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);
        _service = new DigestService(_store, _embedder, _availability);
    }

    public void Dispose() => _dbContext.Dispose();

    private static string Long(string text) => text.PadRight(60, '.');

    private void Indexed(
        DateTime date,
        string text,
        float[] vector,
        string sourceId = "",
        int ordinal = 0,
        string kind = EmbeddingSourceKind.Diary)
    {
        var id = string.IsNullOrEmpty(sourceId) ? date.ToString("yyyyMMdd") : sourceId;
        EmbeddingMath.NormalizeInPlace(vector);
        _store.UpsertBatch([new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(kind, id, ordinal),
            SourceKind = kind,
            SourceId = id,
            Ordinal = ordinal,
            SourceDate = date,
            Text = text,
            ContentHash = "h",
            ModelId = Model,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        }]);
    }

    [Fact]
    public async Task Build_AiUnavailable_ReturnsAnEmptyDigestRatherThanThrowing()
    {
        _availability.CanEmbed = false;

        var digest = await _service.BuildAsync(Start, End);

        digest.HasContent.Should().BeFalse();
        digest.DaysWritten.Should().Be(0);
    }

    [Fact]
    public async Task Build_AiSwitchedOff_StopsQuotingAnIndexThatIsStillOnDisk()
    {
        // Switching AI off does not erase the index — rebuilding it costs a long pass over the
        // whole diary. So the only thing standing between a disabled feature and the user's own
        // words being quoted back at them is this check.
        Indexed(new DateTime(2026, 6, 3), Long("довгий запис про роботу і втому"), [1f, 0f]);
        Indexed(new DateTime(2026, 6, 4), Long("ще один довгий запис про те саме"), [1f, 0f]);
        (await _service.BuildAsync(Start, End)).HasContent.Should().BeTrue("otherwise the test proves nothing");

        _availability.CanEmbed = false;

        (await _service.BuildAsync(Start, End)).HasContent.Should().BeFalse();
    }

    [Fact]
    public async Task Build_NothingInThePeriod_IsEmpty()
    {
        Indexed(new DateTime(2026, 5, 1), Long("травневий запис"), [1f, 0f]);

        (await _service.BuildAsync(Start, End)).HasContent.Should().BeFalse();
    }

    [Fact]
    public async Task Build_CountsDaysWrittenNotChunks()
    {
        Indexed(new DateTime(2026, 6, 2), Long("перший"), [1f, 0f]);
        Indexed(new DateTime(2026, 6, 2), Long("той самий день"), [1f, 0.1f], ordinal: 1);
        Indexed(new DateTime(2026, 6, 5), Long("інший день"), [1f, 0.2f]);

        var digest = await _service.BuildAsync(Start, End);

        digest.DaysWritten.Should().Be(2);
        digest.DaysInPeriod.Should().Be(30);
    }

    [Fact]
    public async Task Build_PrefersWhatThePeriodWasMostlyAbout()
    {
        // Four entries cluster together, one is an outlier. The digest should describe the cluster.
        Indexed(new DateTime(2026, 6, 1), Long("робота і дедлайни один"), [1f, 0f]);
        Indexed(new DateTime(2026, 6, 2), Long("робота і дедлайни два"), [1f, 0.05f]);
        Indexed(new DateTime(2026, 6, 3), Long("робота і дедлайни три"), [1f, 0.1f]);
        Indexed(new DateTime(2026, 6, 4), Long("зовсім про інше"), [0f, 1f]);

        var digest = await _service.BuildAsync(Start, End, maxExcerpts: 1);

        digest.Excerpts.Should().ContainSingle();
        digest.Excerpts[0].Text.Should().Contain("робота");
    }

    [Fact]
    public async Task Build_AtMostOneExcerptPerDay()
    {
        // Three quotations from one afternoon describe an afternoon, not a month.
        Indexed(new DateTime(2026, 6, 10), Long("ранок"), [1f, 0f], ordinal: 0);
        Indexed(new DateTime(2026, 6, 10), Long("день"), [1f, 0.01f], ordinal: 1);
        Indexed(new DateTime(2026, 6, 10), Long("вечір"), [1f, 0.02f], ordinal: 2);
        Indexed(new DateTime(2026, 6, 20), Long("інший день"), [1f, 0.03f]);

        var digest = await _service.BuildAsync(Start, End, maxExcerpts: 3);

        digest.Excerpts.Select(e => e.Date.Date).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Build_ExcerptsAreInChronologicalOrder()
    {
        Indexed(new DateTime(2026, 6, 20), Long("пізніше"), [1f, 0f]);
        Indexed(new DateTime(2026, 6, 5), Long("раніше"), [1f, 0.02f]);

        var digest = await _service.BuildAsync(Start, End, maxExcerpts: 2);

        digest.Excerpts.Select(e => e.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Build_HonoursTheExcerptLimit()
    {
        for (var day = 1; day <= 10; day++)
        {
            Indexed(new DateTime(2026, 6, day), Long($"запис {day}"), [1f, day * 0.01f]);
        }

        (await _service.BuildAsync(Start, End, maxExcerpts: 3)).Excerpts.Should().HaveCount(3);
    }

    [Fact]
    public async Task Build_SkipsFragmentsWhenThereIsSomethingSubstantial()
    {
        Indexed(new DateTime(2026, 6, 3), "борщ, риба", [1f, 0f]);
        Indexed(new DateTime(2026, 6, 4), Long("справжній запис про день"), [1f, 0.01f]);

        var digest = await _service.BuildAsync(Start, End, maxExcerpts: 1);

        digest.Excerpts[0].Text.Should().Contain("справжній");
    }

    [Fact]
    public async Task Build_AllFragments_StillProducesSomething()
    {
        // A month of one-line entries is still a month worth summarising.
        Indexed(new DateTime(2026, 6, 3), "кава", [1f, 0f]);
        Indexed(new DateTime(2026, 6, 4), "дощ", [1f, 0.01f]);

        (await _service.BuildAsync(Start, End)).HasContent.Should().BeTrue();
    }

    [Fact]
    public async Task Build_IgnoresNotes_BecauseADiaryDigestIsAboutDays()
    {
        Indexed(new DateTime(2026, 6, 3), Long("нотатка"), [1f, 0f], kind: EmbeddingSourceKind.Note);

        (await _service.BuildAsync(Start, End)).HasContent.Should().BeFalse();
    }

    [Fact]
    public async Task Build_QuotesVerbatim()
    {
        // The whole reason the digest is extractive: a quotation cannot misrepresent the diary.
        var text = Long("сьогодні я нарешті наважився і подзвонив");
        Indexed(new DateTime(2026, 6, 3), text, [1f, 0f]);

        (await _service.BuildAsync(Start, End)).Excerpts[0].Text.Should().Be(text);
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
