using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class ThemeClusterService : IThemeClusterService
{
    /// <summary>
    /// How close two passages must sit to count as the same theme. Well above the 0.28 search
    /// floor: a search may usefully return something adjacent, but a theme that admits everything
    /// adjacent collapses into one cluster called "life".
    /// </summary>
    public const float ThemeSimilarity = 0.5f;

    /// <summary>A theme has to recur. Something written about on one day is an entry, not a theme.</summary>
    private const int MinDaysPerTheme = 2;

    private const int MaxLabelChars = 90;

    /// <summary>
    /// Below this a passage is an answer, not a subject. Without it the dashboard's top theme came
    /// out as the word "Ні" across 41 days — the one-word intimate-life field, which is both
    /// meaningless as a theme and not something to headline a summary screen with.
    ///
    /// Lower than the digest's forty. That figure is about whether a passage is worth quoting; a
    /// theme only has to be nameable, and forty dropped "Стрес на роботі, затори на дорогах" at
    /// thirty-four characters — a real subject, lost to a threshold borrowed from another job.
    /// </summary>
    private const int MinThemeChars = 20;

    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly IAiAvailability _availability;

    private readonly object _memoLock = new();
    private (string Key, ThemeSummary Result)? _memo;

    public ThemeClusterService(IVectorStore store, ITextEmbedder embedder, IAiAvailability availability)
    {
        _store = store;
        _embedder = embedder;
        _availability = availability;
    }

    public async Task<IReadOnlyList<DiaryTheme>> ClusterAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default) =>
        (await SummariseAsync(start, end, maxThemes, cancellationToken).ConfigureAwait(false)).Themes;

    public async Task<ThemeSummary> SummariseAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThemes, 1);

        var empty = new ThemeSummary([], []);

        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return empty;
        }

        var chunks = _store.GetByDateRange(_embedder.ModelId, start.Date, end.Date, SearchScope.Diary);
        if (chunks.Count == 0)
        {
            return empty;
        }

        var key = MemoKey(start, end, maxThemes, chunks);
        lock (_memoLock)
        {
            if (_memo is { } memo && memo.Key == key)
            {
                return memo.Result;
            }
        }

        // Every day that was written on, whatever it was written about — including days whose
        // passages are all below MinThemeChars. Those are days the theme could have appeared on and
        // did not, which is exactly what a correlation needs.
        var indexedDays = chunks.Select(c => c.SourceDate.Date).Distinct().Order().ToList();

        var dimensions = chunks[0].Dim;
        var points = chunks
            .Where(c => c.Dim == dimensions)
            .Where(c => c.Text.Trim().Length >= MinThemeChars)
            .Select(c => new Point(c, EmbeddingMath.FromBytes(c.Vector)))
            .ToList();

        // Clustering runs outside the lock: it is the second or two this memo exists to avoid, and
        // two callers racing here duplicate work rather than corrupt anything.
        var result = new ThemeSummary(Cluster(points, maxThemes, cancellationToken), indexedDays);

        lock (_memoLock)
        {
            _memo = (key, result);
        }

        return result;
    }

    /// <summary>
    /// Identifies a window and its contents well enough to reuse the clustering. The statistics
    /// screen asks for the same window twice in a row — once for the digest, once for the mood
    /// correlations — and re-reading the rows is cheap while re-clustering them is O(k·n²).
    /// </summary>
    /// <remarks>
    /// Chunk count and total length are a heuristic signature, not a hash: an edit that replaces a
    /// passage with one of exactly the same length leaves the themes stale until the window or the
    /// count changes. That is an acceptable trade on a statistics screen, and cheaper than hashing
    /// every vector on every call.
    /// </remarks>
    private string MemoKey(DateTime start, DateTime end, int maxThemes, IReadOnlyList<EmbeddingChunk> chunks)
    {
        long totalChars = 0;
        foreach (var c in chunks)
        {
            totalChars += c.Text.Length;
        }

        return $"{_embedder.ModelId}|{start.Date:O}|{end.Date:O}|{maxThemes}|{chunks.Count}|{totalChars}";
    }

    /// <summary>
    /// Greedy densest-first clustering: repeatedly take the passage with the most neighbours, claim
    /// them as a theme, remove them, repeat.
    /// </summary>
    /// <remarks>
    /// Deliberately not k-means or k-medoids. Both need a random start and a number of clusters
    /// decided in advance; this needs neither, gives the same answer every run, and lets the data
    /// say how many themes there were — including none.
    /// </remarks>
    private static IReadOnlyList<DiaryTheme> Cluster(
        List<Point> points,
        int maxThemes,
        CancellationToken cancellationToken)
    {
        var themes = new List<DiaryTheme>();
        var remaining = points;

        while (themes.Count < maxThemes && remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var best = remaining
                .Select(seed => new
                {
                    Seed = seed,
                    Members = remaining
                        .Where(other => EmbeddingMath.DotNormalized(seed.Vector, other.Vector) >= ThemeSimilarity)
                        .ToList(),
                })
                // Ties broken by date so the result never depends on collection order.
                .OrderByDescending(c => c.Members.Select(m => m.Chunk.SourceDate.Date).Distinct().Count())
                .ThenBy(c => c.Seed.Chunk.SourceDate)
                .First();

            var days = best.Members.Select(m => m.Chunk.SourceDate.Date).Distinct().Order().ToList();
            if (days.Count < MinDaysPerTheme)
            {
                // The densest cluster left does not recur, so nothing after it will either.
                break;
            }

            themes.Add(new DiaryTheme(Label(best.Seed.Chunk.Text), days));

            var claimed = best.Members.ToHashSet();
            remaining = remaining.Where(p => !claimed.Contains(p)).ToList();
        }

        return themes.OrderByDescending(t => t.DayCount).ThenBy(t => t.FirstSeen).ToList();
    }

    /// <summary>First sentence of the passage, trimmed. The theme is named in the user's own words.</summary>
    private static string Label(string text)
    {
        var trimmed = text.Trim();
        var stop = trimmed.IndexOfAny(['.', '!', '?', '\n']);
        var sentence = stop > 0 ? trimmed[..stop] : trimmed;

        return sentence.Length <= MaxLabelChars
            ? sentence
            : sentence[..MaxLabelChars].TrimEnd() + "…";
    }

    private sealed record Point(EmbeddingChunk Chunk, float[] Vector);
}
