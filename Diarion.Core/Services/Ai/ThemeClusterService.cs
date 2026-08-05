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
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxThemes, 1);

        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return [];
        }

        var chunks = _store.GetByDateRange(_embedder.ModelId, start.Date, end.Date, SearchScope.Diary);
        if (chunks.Count == 0)
        {
            return [];
        }

        var dimensions = chunks[0].Dim;
        var points = chunks
            .Where(c => c.Dim == dimensions)
            .Where(c => c.Text.Trim().Length >= MinThemeChars)
            .Select(c => new Point(c, EmbeddingMath.FromBytes(c.Vector)))
            .ToList();

        return Cluster(points, maxThemes, cancellationToken);
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

            var days = best.Members.Select(m => m.Chunk.SourceDate.Date).Distinct().ToList();
            if (days.Count < MinDaysPerTheme)
            {
                // The densest cluster left does not recur, so nothing after it will either.
                break;
            }

            themes.Add(new DiaryTheme(
                Label(best.Seed.Chunk.Text),
                days.Count,
                days.Min(),
                days.Max()));

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
