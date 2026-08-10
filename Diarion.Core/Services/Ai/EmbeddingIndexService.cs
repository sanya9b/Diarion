using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class EmbeddingIndexService : IEmbeddingIndexService
{
    /// <summary>
    /// Chunks embedded and written per transaction. Small on purpose: a kill mid-run costs one
    /// batch, and the next pass rediscovers it from the hash comparison.
    /// </summary>
    private const int BatchSize = 8;

    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly IDiaryService _diaryService;
    private readonly INoteService _noteService;
    private readonly IAiAvailability _availability;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Coalesces the flurry of saves autosave produces. It fires on every property change in the
    /// entry view model, so an unthrottled hook would re-embed the same day on every keystroke.
    /// Keyed by document, so editing one entry never delays re-indexing another.
    /// </summary>
    private readonly Dictionary<string, AsyncDebouncer> _pendingReindex = new();
    private readonly object _pendingLock = new();
    private readonly TimeSpan _reindexDelay;
    private CancellationTokenSource? _cts;
    private Task _running = Task.CompletedTask;

    public EmbeddingIndexService(
        IVectorStore store,
        ITextEmbedder embedder,
        IDiaryService diaryService,
        INoteService noteService,
        IAiAvailability availability,
        TimeSpan? reindexDelay = null)
    {
        _reindexDelay = reindexDelay ?? TimeSpan.FromSeconds(2);
        _store = store;
        _embedder = embedder;
        _diaryService = diaryService;
        _noteService = noteService;
        _availability = availability;

        WeakReferenceMessenger.Default.Register<DocumentChangedMessage>(this, (r, m) =>
            QueueReindex(m.SourceKind, m.SourceId));
    }

    private void QueueReindex(string sourceKind, string sourceId)
    {
        AsyncDebouncer debouncer;
        var key = LiteDbVectorStore.SourceKey(sourceKind, sourceId);

        lock (_pendingLock)
        {
            if (!_pendingReindex.TryGetValue(key, out debouncer!))
            {
                debouncer = new AsyncDebouncer(_reindexDelay);
                _pendingReindex[key] = debouncer;
            }
        }

        debouncer.Debounce(() => ReindexSourceAsync(sourceKind, sourceId));
    }

    public AiIndexProgress Progress { get; private set; } = AiIndexProgress.Idle;

    public event EventHandler<AiIndexProgress>? ProgressChanged;

    public void Start()
    {
        // Availability is checked again inside the run, together with consent. This early exit is
        // only to avoid spawning a task per resume once one is already going.
        if (!_embedder.IsAvailable || !_running.IsCompleted)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _running = Task.Run(async () =>
        {
            try
            {
                await RunOnceAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Report(new AiIndexProgress(AiIndexPhase.Cancelled, Progress.Done, Progress.Total));
            }
        }, token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _running.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: this is how a cancelled run ends.
        }

        _cts.Dispose();
        _cts = null;
    }

    public async Task<AiIndexProgress> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return Report(AiIndexProgress.Idle);
        }

        // Serialised against ReindexSourceAsync and against a second Start(): two writers deriving
        // their queues from the same hashes would embed the same chunks twice.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Report(new AiIndexProgress(AiIndexPhase.Scanning, 0, 0));

            var modelId = _embedder.ModelId;

            // Rows from a previous model are not comparable to the current vector space, and
            // leaving them costs storage while never being searchable.
            _store.DeleteForeignModels(modelId);

            var stale = await FindStaleDocumentsAsync(modelId, cancellationToken).ConfigureAwait(false);
            if (stale.Count == 0)
            {
                return Report(new AiIndexProgress(AiIndexPhase.Complete, 0, 0));
            }

            var done = 0;
            Report(new AiIndexProgress(AiIndexPhase.Embedding, done, stale.Count));

            foreach (var document in stale)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await IndexDocumentAsync(document, modelId, cancellationToken).ConfigureAwait(false);

                done++;
                Report(new AiIndexProgress(AiIndexPhase.Embedding, done, stale.Count));
            }

            return Report(new AiIndexProgress(AiIndexPhase.Complete, done, stale.Count));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReindexSourceAsync(string sourceKind, string sourceId, CancellationToken cancellationToken = default)
    {
        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return;
        }

        var document = await LoadDocumentAsync(sourceKind, sourceId).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (document is null)
            {
                // Deleted, or edited down to nothing: its rows would otherwise keep surfacing in
                // search as text the user can no longer open.
                _store.DeleteBySource(sourceKind, sourceId);
                return;
            }

            await IndexDocumentAsync(document, _embedder.ModelId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _store.Clear();
        Report(AiIndexProgress.Idle);
    }

    private async Task<IReadOnlyList<IndexableDocument>> FindStaleDocumentsAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        var indexed = _store.GetIndexedHashes(modelId);
        var stale = new List<IndexableDocument>();

        foreach (var entry in await _diaryService.GetAllEntriesAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Rows are created just by browsing to a date, so "a row exists" is not "the user wrote
            // something" — HasContent is the same gate streaks use.
            if (!entry.HasContent())
            {
                continue;
            }

            AddIfStale(stale, indexed, Describe(entry));
        }

        foreach (var note in await _noteService.GetAllNotesAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddIfStale(stale, indexed, Describe(note));
        }

        return stale;
    }

    private static void AddIfStale(
        List<IndexableDocument> stale,
        IReadOnlyDictionary<string, string> indexed,
        IndexableDocument document)
    {
        if (document.Segments.Count == 0)
        {
            return;
        }

        var key = LiteDbVectorStore.SourceKey(document.SourceKind, document.SourceId);
        if (indexed.TryGetValue(key, out var storedHash) && storedHash == document.ContentHash)
        {
            return;
        }

        stale.Add(document);
    }

    private async Task IndexDocumentAsync(IndexableDocument document, string modelId, CancellationToken cancellationToken)
    {
        var chunks = TextChunker.Chunk(document.Segments);

        // Replace rather than upsert: an edit that shortens a document would otherwise leave its
        // surplus chunks behind, still matching text that no longer exists.
        _store.DeleteBySource(document.SourceKind, document.SourceId);

        if (chunks.Count == 0)
        {
            return;
        }

        for (var offset = 0; offset < chunks.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = chunks.Skip(offset).Take(BatchSize).ToList();
            var vectors = await _embedder.EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            var rows = batch.Select((text, i) => new EmbeddingChunk
            {
                Id = EmbeddingChunk.BuildId(document.SourceKind, document.SourceId, offset + i),
                SourceKind = document.SourceKind,
                SourceId = document.SourceId,
                Ordinal = offset + i,
                SourceDate = document.SourceDate,
                Text = text,
                ContentHash = document.ContentHash,
                ModelId = modelId,
                Dim = vectors[i].Length,
                Vector = EmbeddingMath.ToBytes(vectors[i]),
                IndexedAtUtc = DateTime.UtcNow,
            });

            _store.UpsertBatch(rows.ToList());
        }
    }

    private async Task<IndexableDocument?> LoadDocumentAsync(string sourceKind, string sourceId)
    {
        switch (sourceKind)
        {
            case EmbeddingSourceKind.Diary when Guid.TryParse(sourceId, out var entryId):
            {
                var entry = await _diaryService.GetEntryByIdAsync(entryId).ConfigureAwait(false);
                if (entry is null || !entry.HasContent())
                {
                    return null;
                }

                var document = Describe(entry);
                return document.Segments.Count == 0 ? null : document;
            }

            case EmbeddingSourceKind.Note:
            {
                var note = await _noteService.GetNoteAsync(sourceId).ConfigureAwait(false);
                if (note is null)
                {
                    return null;
                }

                var document = Describe(note);
                return document.Segments.Count == 0 ? null : document;
            }

            default:
                return null;
        }
    }

    private static IndexableDocument Describe(DiaryEntry entry)
    {
        var segments = IndexableTextComposer.ComposeEntry(entry);
        return new IndexableDocument(
            EmbeddingSourceKind.Diary,
            entry.Id.ToString(),
            entry.Date,
            segments,
            IndexableTextComposer.ComputeHash(segments));
    }

    private static IndexableDocument Describe(Note note)
    {
        var segments = IndexableTextComposer.ComposeNote(note);
        return new IndexableDocument(
            EmbeddingSourceKind.Note,
            note.Id.ToString(),
            note.UpdatedAt,
            segments,
            IndexableTextComposer.ComputeHash(segments));
    }

    private AiIndexProgress Report(AiIndexProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(this, progress);
        return progress;
    }

    private sealed record IndexableDocument(
        string SourceKind,
        string SourceId,
        DateTime SourceDate,
        IReadOnlyList<string> Segments,
        string ContentHash);
}
