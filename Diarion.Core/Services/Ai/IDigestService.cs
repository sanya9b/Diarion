using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>One line of a period's digest, quoted verbatim from the user's own writing.</summary>
/// <param name="Date">The day the excerpt came from.</param>
/// <param name="Text">The user's words, untouched.</param>
public sealed record DigestExcerpt(DateTime Date, string Text);

/// <param name="Start">Inclusive.</param>
/// <param name="End">Inclusive.</param>
/// <param name="DaysWritten">Days that actually have something in them, not calendar days.</param>
/// <param name="Excerpts">Representative passages, most central first.</param>
public sealed record Digest(
    DateTime Start,
    DateTime End,
    int DaysWritten,
    int DaysInPeriod,
    IReadOnlyList<DigestExcerpt> Excerpts)
{
    public bool HasContent => Excerpts.Count > 0;
}

/// <summary>
/// Builds the weekly and monthly summary of what a period was about.
/// </summary>
/// <remarks>
/// Extractive, not generated. The digest quotes the user's own sentences, chosen by how close they
/// sit to the period's centroid, which means weekly and monthly reports work on every device and
/// both platforms with no generative model at all — and a quotation cannot misrepresent a diary
/// the way a paraphrase from a small model can.
/// </remarks>
public interface IDigestService
{
    Task<Digest> BuildAsync(
        DateTime start,
        DateTime end,
        int maxExcerpts = 3,
        CancellationToken cancellationToken = default);
}
