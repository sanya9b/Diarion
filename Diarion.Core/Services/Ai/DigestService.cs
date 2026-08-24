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

        // Off the calling thread from here on. The statistics screen calls this from the UI thread
        // deliberately — every tab writes bound collections and those writes have to land there — but
        // the read below scans the whole embedding collection and the centroid pass touches every
        // vector in the window. Left inline that is a frozen screen for as long as it takes, which on
        // a phone is not slowness but a hang: Android raises ANR and the iOS watchdog kills the app.
        var modelId = _embedder.ModelId;
        return await Task.Run(
            () => Build(modelId, from, to, daysInPeriod, maxExcerpts, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private Digest Build(
        string modelId,
        DateTime from,
        DateTime to,
        int daysInPeriod,
        int maxExcerpts,
        CancellationToken cancellationToken)
    {
        var chunks = _store.GetByDateRange(modelId, from, to, SearchScope.Diary);
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
        // A row's Dim and the width of its blob can disagree — a leftover from an earlier model that
        // shared an id, which is why the search path skips those too. Here the mismatch would reach
        // Dot() and throw on the length check, and an exception from this call does not degrade the
        // digest, it stops the statistics screen from opening.
        var reference = chunks.FirstOrDefault(c => c.Vector.Length == c.Dim * sizeof(float));
        if (reference is null)
        {
            return [];
        }

        var dimensions = reference.Dim;
        var usable = chunks
            .Where(c => c.Dim == dimensions && c.Vector.Length == dimensions * sizeof(float))
            .ToList();
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
