using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class DigestService : IDigestService
{
    /// <summary>
    /// Chunks shorter than this are fragments — a meal list, a two-word note — and read as filler
    /// when quoted back as "what this month was about".
    /// </summary>
    private const int MinExcerptChars = 40;

    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly IAiAvailability _availability;

    public DigestService(IVectorStore store, ITextEmbedder embedder, IAiAvailability availability)
    {
        _store = store;
        _embedder = embedder;
        _availability = availability;
    }

    public async Task<Digest> BuildAsync(
        DateTime start,
        DateTime end,
        int maxExcerpts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExcerpts, 1);

        var from = start.Date;
        var to = end.Date;
        var daysInPeriod = Math.Max(1, (to - from).Days + 1);

        if (!await _availability.CanEmbedAsync().ConfigureAwait(false))
        {
            return new Digest(from, to, 0, daysInPeriod, []);
        }

        var chunks = _store.GetByDateRange(_embedder.ModelId, from, to, SearchScope.Diary);
        if (chunks.Count == 0)
        {
            return new Digest(from, to, 0, daysInPeriod, []);
        }

        var daysWritten = chunks.Select(c => c.SourceDate.Date).Distinct().Count();

        var candidates = chunks.Where(c => c.Text.Length >= MinExcerptChars).ToList();

        // Everything was short — a month of one-line entries is still a month worth summarising, so
        // fall back to what there is rather than returning nothing.
        if (candidates.Count == 0)
        {
            candidates = chunks.ToList();
        }

        var excerpts = SelectCentral(candidates, maxExcerpts, cancellationToken);

        return new Digest(from, to, daysWritten, daysInPeriod, excerpts);
    }

    /// <summary>
    /// Picks the passages closest to the period's centre of mass — the things the period was
    /// mostly about, rather than its oddities. One per day at most: three quotations from the same
    /// afternoon describe an afternoon, not a month.
    /// </summary>
    private static IReadOnlyList<DigestExcerpt> SelectCentral(
        IReadOnlyList<EmbeddingChunk> chunks,
        int maxExcerpts,
        CancellationToken cancellationToken)
    {
        var dimensions = chunks[0].Dim;
        var usable = chunks.Where(c => c.Dim == dimensions).ToList();
        if (usable.Count == 0)
        {
            return [];
        }

        var centroid = new float[dimensions];
        foreach (var chunk in usable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = EmbeddingMath.FromBytes(chunk.Vector);
            for (var d = 0; d < dimensions; d++)
            {
                centroid[d] += vector[d];
            }
        }

        EmbeddingMath.NormalizeInPlace(centroid);

        return usable
            .Select(c => (Chunk: c, Score: EmbeddingMath.DotNormalized(centroid, EmbeddingMath.FromBytes(c.Vector))))
            .OrderByDescending(x => x.Score)
            .GroupBy(x => x.Chunk.SourceDate.Date)
            .Select(g => g.First())
            .OrderByDescending(x => x.Score)
            .Take(maxExcerpts)
            .OrderBy(x => x.Chunk.SourceDate)
            .Select(x => new DigestExcerpt(x.Chunk.SourceDate, x.Chunk.Text))
            .ToList();
    }
}
