using System;
using System.Collections.Generic;

namespace Diarion.Services.InsightPath;

/// <summary>
/// The four points on the way to a first hypothesis, counted in paired days — a mood and the same
/// factor recorded on one day (spec 17, D2–D3). Each opens something the app already has: the first
/// pixel, the emotion donut, mood by hour and weekday, the hypothesis itself. So reaching one is
/// never an empty congratulation, and missing a day never moves anyone back.
/// </summary>
/// <remarks>
/// Pure on purpose: no clock, no profile, no services. The service decides what is visible; this
/// type only answers where a given count stands. If four milestones prove two too many, the table
/// changes here and nowhere else.
/// </remarks>
public static class InsightPathMilestones
{
    /// <summary>
    /// Ascending. The last one is where the hypothesis appears, so it has to equal the engine's
    /// <c>CorrelationService.MinSampleSize</c>; a test holds the two together rather than a
    /// reference, to keep this type free of the engine.
    /// </summary>
    public static IReadOnlyList<int> All { get; } = Array.AsReadOnly(new[] { 1, 3, 7, 14 });

    /// <summary>The milestone that ends the path.</summary>
    public static int Last => All[^1];

    /// <summary>The highest milestone <paramref name="pairedDays"/> has reached, or 0 before the first.</summary>
    public static int Reached(int pairedDays)
    {
        var reached = 0;
        foreach (var milestone in All)
        {
            if (pairedDays >= milestone)
            {
                reached = milestone;
            }
        }

        return reached;
    }

    /// <summary>The first milestone still ahead of <paramref name="pairedDays"/>, or null past the last.</summary>
    public static int? Next(int pairedDays)
    {
        foreach (var milestone in All)
        {
            if (pairedDays < milestone)
            {
                return milestone;
            }
        }

        return null;
    }
}
