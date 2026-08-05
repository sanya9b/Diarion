using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class SemanticSearchService : ISemanticSearchService
{
    /// <summary>
    /// Similarity below which a vector match is noise rather than a weak answer. Measured, not
    /// guessed: a relevant single-word query scored 0.306 against its entry while unrelated text
    /// reached 0.259, so anything higher than this starts discarding true matches.
    /// See specs/13-on-device-ai.md.
    /// </summary>
    public const float MinSemanticScore = 0.28f;

    /// <summary>
    /// Reciprocal-rank-fusion damping. The conventional 60: large enough that the top few ranks of
    /// each list are worth roughly the same, so neither ranker can bully the other.
    /// </summary>
    private const int RrfDamping = 60;

    /// <summary>Chunks pulled before fusion. Wider than the result count so fusion has something to do.</summary>
    private const int VectorCandidates = 60;

    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly IDiaryService _diaryService;
    private readonly INoteService _noteService;
    private readonly IAiAvailability _availability;

    public SemanticSearchService(
        IVectorStore store,
        ITextEmbedder embedder,
        IDiaryService diaryService,
        INoteService noteService,
        IAiAvailability availability)
    {
        _store = store;
        _embedder = embedder;
        _diaryService = diaryService;
        _noteService = noteService;
        _availability = availability;
    }

    public Task<bool> IsSemanticAvailableAsync() => _availability.CanEmbedAsync();

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SearchScope scope = SearchScope.All,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var lexical = await SearchLexicallyAsync(query, scope, cancellationToken).ConfigureAwait(false);
        var semantic = await SearchSemanticallyAsync(query, scope, cancellationToken).ConfigureAwait(false);

        return Fuse(lexical, semantic, limit);
    }

    /// <summary>
    /// Reciprocal rank fusion. Ranks are combined rather than scores because a cosine similarity
    /// and a substring hit are not on the same scale and never will be — normalising them against
    /// each other would be inventing a number.
    /// </summary>
    private static IReadOnlyList<SearchHit> Fuse(
        IReadOnlyList<SearchHit> lexical,
        IReadOnlyList<SearchHit> semantic,
        int limit)
    {
        var fused = new Dictionary<string, (SearchHit Hit, double Score)>();

        void Accumulate(IReadOnlyList<SearchHit> ranked)
        {
            for (var rank = 0; rank < ranked.Count; rank++)
            {
                var hit = ranked[rank];
                var key = $"{hit.SourceKind}|{hit.SourceId}";
                var contribution = 1d / (RrfDamping + rank + 1);

                if (fused.TryGetValue(key, out var existing))
                {
                    // Keep whichever snippet scored higher on its own list; the semantic one is
                    // usually the more informative fragment, the lexical one the literal match.
                    var better = hit.Score > existing.Hit.Score ? hit : existing.Hit;
                    fused[key] = (better, existing.Score + contribution);
                }
                else
                {
                    fused[key] = (hit, contribution);
                }
            }
        }

        Accumulate(semantic);
        Accumulate(lexical);

        return fused.Values
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Hit.SourceDate)
            .Take(limit)
            .Select(x => x.Hit)
            .ToList();
    }

    private async Task<IReadOnlyList<SearchHit>> SearchSemanticallyAsync(
        string query,
        SearchScope scope,
        CancellationToken cancellationToken)
    {
        // Only this half is gated. Lexical search over notes predates the AI module and is not part
        // of it, so switching AI off must narrow the results, not break the screen.
        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return [];
        }

        var vector = await _embedder.EmbedAsync(query, cancellationToken).ConfigureAwait(false);

        var chunks = _store.Search(
            vector,
            _embedder.ModelId,
            VectorCandidates,
            scope,
            MinSemanticScore);

        // Several chunks of one entry are one result. Keeping them separate would let a long entry
        // occupy the whole first screen.
        return chunks
            .GroupBy(c => $"{c.Chunk.SourceKind}|{c.Chunk.SourceId}")
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score)
            .Select(c => new SearchHit(
                c.Chunk.SourceKind,
                c.Chunk.SourceId,
                c.Chunk.SourceDate,
                Snippet(c.Chunk.Text),
                c.Score))
            .ToList();
    }

    private async Task<IReadOnlyList<SearchHit>> SearchLexicallyAsync(
        string query,
        SearchScope scope,
        CancellationToken cancellationToken)
    {
        var terms = query
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var hits = new List<SearchHit>();

        if (scope.HasFlag(SearchScope.Notes))
        {
            // Reuses the notes search that already exists rather than reimplementing it, so the two
            // entry points cannot drift apart.
            foreach (var note in await _noteService.SearchNotesAsync(query).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                hits.Add(new SearchHit(
                    EmbeddingSourceKind.Note,
                    note.Id.ToString(),
                    note.UpdatedAt,
                    Snippet(string.IsNullOrWhiteSpace(note.Content) ? note.Title : note.Content),
                    1f));
            }
        }

        if (scope.HasFlag(SearchScope.Diary))
        {
            foreach (var entry in await _diaryService.GetAllEntriesAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.HasContent())
                {
                    continue;
                }

                var segments = IndexableTextComposer.ComposeEntry(entry);
                var matched = segments.FirstOrDefault(s => terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase)));
                if (matched is null)
                {
                    continue;
                }

                hits.Add(new SearchHit(
                    EmbeddingSourceKind.Diary,
                    entry.Id.ToString(),
                    entry.Date,
                    Snippet(matched),
                    1f));
            }
        }

        return hits.OrderByDescending(h => h.SourceDate).ToList();
    }

    private const int SnippetLength = 180;

    private static string Snippet(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= SnippetLength ? trimmed : trimmed[..SnippetLength].TrimEnd() + "…";
    }
}
