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
using LiteDB;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class EmbeddingIndexServiceTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 7, 15);

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;
    private readonly FakeEmbedder _embedder = new();
    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<INoteService> _notes = new();
    private readonly EmbeddingIndexService _service;

    public EmbeddingIndexServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);

        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(new List<DiaryEntry>());
        _notes.Setup(n => n.GetAllNotesAsync()).ReturnsAsync(new List<Note>());

        _service = new EmbeddingIndexService(_store, _embedder, _diary.Object, _notes.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    private void HasEntries(params DiaryEntry[] entries) =>
        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(entries.ToList());

    private void HasNotes(params Note[] notes) =>
        _notes.Setup(n => n.GetAllNotesAsync()).ReturnsAsync(notes.ToList());

    private static DiaryEntry Entry(string gratitude, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Date = Day,
        Gratitude = gratitude,
    };

    [Fact]
    public async Task RunOnce_EmbedsEntriesThatHaveContent()
    {
        HasEntries(Entry("за каву"));

        var progress = await _service.RunOnceAsync();

        progress.Phase.Should().Be(AiIndexPhase.Complete);
        _store.CountForModel(FakeEmbedder.Id).Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_SkipsEntriesTheUserNeverWroteIn()
    {
        // Rows are created just by browsing to a date, so an empty row is not a journal entry.
        HasEntries(new DiaryEntry { Date = Day, CycleDay = "12" });

        await _service.RunOnceAsync();

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
        _embedder.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_IndexesNotesToo()
    {
        HasNotes(new Note { Title = "Ідеї", Content = "купити книгу", UpdatedAt = Day });

        await _service.RunOnceAsync();

        _store.CountForModel(FakeEmbedder.Id).Should().Be(2);
    }

    [Fact]
    public async Task RunOnce_SecondPassWithUnchangedData_EmbedsNothing()
    {
        // The whole resumability story: the queue is derived from hashes, so a repeat pass is free.
        HasEntries(Entry("за каву"));
        await _service.RunOnceAsync();
        var callsAfterFirst = _embedder.CallCount;

        var progress = await _service.RunOnceAsync();

        _embedder.CallCount.Should().Be(callsAfterFirst);
        progress.Total.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_EditedEntry_IsReEmbedded()
    {
        var id = Guid.NewGuid();
        HasEntries(Entry("за каву", id));
        await _service.RunOnceAsync();

        HasEntries(Entry("за спокійний вечір", id));
        await _service.RunOnceAsync();

        var stored = _store.Search(_embedder.Embed("за спокійний вечір"), FakeEmbedder.Id, limit: 10);
        stored.Should().ContainSingle();
        stored[0].Chunk.Text.Should().Be("за спокійний вечір");
    }

    [Fact]
    public async Task RunOnce_ShortenedEntry_LeavesNoOrphanChunks()
    {
        var id = Guid.NewGuid();
        var longText = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"слово{i}"));
        HasEntries(Entry(longText, id));
        await _service.RunOnceAsync();
        _store.CountForModel(FakeEmbedder.Id).Should().BeGreaterThan(1);

        HasEntries(Entry("коротко", id));
        await _service.RunOnceAsync();

        // Surplus chunks would keep matching text the entry no longer contains.
        _store.CountForModel(FakeEmbedder.Id).Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_ModelChanged_DiscardsTheOldVectorSpace()
    {
        HasEntries(Entry("за каву"));
        await _service.RunOnceAsync();

        _embedder.SetModel("другa-модель");
        await _service.RunOnceAsync();

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
        _store.CountForModel("другa-модель").Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_LongEntry_IsSplitAcrossChunksThatAllShareTheDocumentHash()
    {
        var longText = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"слово{i}"));
        HasEntries(Entry(longText));

        await _service.RunOnceAsync();

        var hashes = _store.GetIndexedHashes(FakeEmbedder.Id);
        hashes.Should().ContainSingle();
        _store.CountForModel(FakeEmbedder.Id).Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task RunOnce_ReportsProgressAndEndsComplete()
    {
        HasEntries(Entry("за каву"), Entry("за прогулянку"));
        var seen = new List<AiIndexProgress>();
        _service.ProgressChanged += (_, p) => seen.Add(p);

        await _service.RunOnceAsync();

        seen.Should().Contain(p => p.Phase == AiIndexPhase.Scanning);
        seen.Last().Phase.Should().Be(AiIndexPhase.Complete);
        seen.Last().Fraction.Should().Be(1d);
        _service.Progress.Phase.Should().Be(AiIndexPhase.Complete);
    }

    [Fact]
    public async Task RunOnce_NoModelInstalled_IsAnIdleNoOp()
    {
        HasEntries(Entry("за каву"));
        var service = new EmbeddingIndexService(_store, new NullTextEmbedder(), _diary.Object, _notes.Object);

        var progress = await service.RunOnceAsync();

        progress.Phase.Should().Be(AiIndexPhase.Idle);
        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_Cancelled_Throws_AndKeepsWhatItAlreadyWrote()
    {
        HasEntries(Entry("за каву"), Entry("за прогулянку"), Entry("за тишу"));
        using var cts = new CancellationTokenSource();
        _embedder.OnEmbed = () =>
        {
            if (_embedder.CallCount >= 1)
            {
                cts.Cancel();
            }
        };

        var act = async () => await _service.RunOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.CountForModel(FakeEmbedder.Id).Should().Be(1);
    }

    [Fact]
    public async Task ReindexSource_DeletedEntry_RemovesItsChunks()
    {
        var id = Guid.NewGuid();
        HasEntries(Entry("за каву", id));
        await _service.RunOnceAsync();
        _diary.Setup(d => d.GetEntryByIdAsync(id)).ReturnsAsync((DiaryEntry)null!);

        await _service.ReindexSourceAsync(EmbeddingSourceKind.Diary, id.ToString());

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
    }

    [Fact]
    public async Task ReindexSource_EmptiedEntry_RemovesItsChunks()
    {
        var id = Guid.NewGuid();
        HasEntries(Entry("за каву", id));
        await _service.RunOnceAsync();
        _diary.Setup(d => d.GetEntryByIdAsync(id)).ReturnsAsync(new DiaryEntry { Id = id, Date = Day });

        await _service.ReindexSourceAsync(EmbeddingSourceKind.Diary, id.ToString());

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
    }

    [Fact]
    public async Task ReindexSource_EditedEntry_ReplacesItsChunks()
    {
        var id = Guid.NewGuid();
        HasEntries(Entry("за каву", id));
        await _service.RunOnceAsync();
        _diary.Setup(d => d.GetEntryByIdAsync(id)).ReturnsAsync(Entry("за тишу", id));

        await _service.ReindexSourceAsync(EmbeddingSourceKind.Diary, id.ToString());

        _store.CountForModel(FakeEmbedder.Id).Should().Be(1);
        _store.Search(_embedder.Embed("за тишу"), FakeEmbedder.Id, limit: 5)[0].Chunk.Text.Should().Be("за тишу");
    }

    [Fact]
    public async Task ReindexSource_UnparsableId_IsIgnored()
    {
        await _service.ReindexSourceAsync(EmbeddingSourceKind.Diary, "not-a-guid");

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
    }

    [Fact]
    public async Task ReindexSource_Note_ReplacesItsChunks()
    {
        var noteId = ObjectId.NewObjectId();
        HasNotes(new Note { Id = noteId, Title = "Ідеї", UpdatedAt = Day });
        await _service.RunOnceAsync();
        _notes.Setup(n => n.GetNoteAsync(noteId.ToString()))
            .ReturnsAsync(new Note { Id = noteId, Title = "Плани", UpdatedAt = Day });

        await _service.ReindexSourceAsync(EmbeddingSourceKind.Note, noteId.ToString());

        _store.Search(_embedder.Embed("Плани"), FakeEmbedder.Id, limit: 5)[0].Chunk.Text.Should().Be("Плани");
    }

    [Fact]
    public async Task Clear_EmptiesTheIndexAndGoesIdle()
    {
        HasEntries(Entry("за каву"));
        await _service.RunOnceAsync();

        await _service.ClearAsync();

        _store.CountForModel(FakeEmbedder.Id).Should().Be(0);
        _service.Progress.Phase.Should().Be(AiIndexPhase.Idle);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsHarmless()
    {
        await _service.StopAsync();

        _service.Progress.Phase.Should().Be(AiIndexPhase.Idle);
    }

    /// <summary>
    /// Deterministic stand-in for the ONNX encoder: the same text always yields the same vector, so
    /// tests can look a chunk up by embedding its text again.
    /// </summary>
    private sealed class FakeEmbedder : ITextEmbedder
    {
        public const string Id = "fake-model";

        public string ModelId { get; private set; } = Id;

        public int Dimensions => 4;

        public bool IsAvailable => true;

        public int CallCount { get; private set; }

        public Action? OnEmbed { get; set; }

        public void SetModel(string modelId) => ModelId = modelId;

        public float[] Embed(string text)
        {
            var vector = new float[Dimensions];
            foreach (var (ch, i) in text.Select((c, i) => (c, i)))
            {
                vector[i % Dimensions] += ch;
            }

            EmbeddingMath.NormalizeInPlace(vector);
            return vector;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Embed(text));

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            OnEmbed?.Invoke();
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());
        }
    }
}
