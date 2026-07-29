using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Models;

/// <summary>What the forecast is standing on, so the UI can always say where its number came from.</summary>
public enum CycleForecastBasis
{
    /// <summary>Nothing logged yet — there is no forecast to give.</summary>
    None,

    /// <summary>One episode, so the cycle length comes from the user's setting rather than their history.</summary>
    ProfileDefault,

    /// <summary>Exactly one usable interval: a real measurement, but not enough to speak of variability.</summary>
    SingleCycle,

    /// <summary>Two or more usable intervals, averaged.</summary>
    Averaged
}

/// <summary>One period: when it started, the last day logged for it, and how many days were marked.
/// <see cref="Length"/> can be smaller than the span when a day inside the period went unlogged.</summary>
public class CycleEpisode
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// The episodes and interval statistics derived from the logged days. Built once and reused, because the
/// calendar asks about forty-two days at a time and rebuilding this per day would be wasteful.
/// </summary>
public class CycleHistory
{
    public static readonly CycleHistory Empty = new();

    public IReadOnlyList<CycleEpisode> Episodes { get; init; } = Array.Empty<CycleEpisode>();

    /// <summary>Days between consecutive episode starts, after implausible ones were discarded.</summary>
    public IReadOnlyList<int> UsableIntervals { get; init; } = Array.Empty<int>();

    /// <summary>How many intervals were dropped as implausible — the signal that logging has gaps.</summary>
    public int DiscardedIntervals { get; init; }

    public IReadOnlyCollection<DateTime> MarkedDates { get; init; } = Array.Empty<DateTime>();

    /// <summary>Set-backed so the calendar, which asks about forty-two days in a row, stays cheap.</summary>
    public bool IsMarked(DateTime date) =>
        MarkedDates is HashSet<DateTime> set ? set.Contains(date.Date) : MarkedDates.Contains(date.Date);
}

/// <summary>The forecast as a single day sees it.</summary>
public class CycleForecast
{
    public bool IsAvailable { get; set; }

    /// <summary>Day of the current cycle, counted from the last episode start. Never wraps.</summary>
    public int CycleDay { get; set; }

    /// <summary>This exact day was logged by the user.</summary>
    public bool IsPeriodDay { get; set; }

    /// <summary>Falls inside the predicted next period, which has not happened yet.</summary>
    public bool IsPredictedPeriodDay { get; set; }

    public bool IsFertileWindowEstimate { get; set; }

    public DateTime? PredictedNextStart { get; set; }

    /// <summary>Half-width of the prediction range in days; 0 when there is no basis for a range.</summary>
    public int UncertaintyDays { get; set; }

    public double AverageCycleLength { get; set; }

    /// <summary>Completed cycles the average rests on.</summary>
    public int RecordedCycleCount { get; set; }

    public CycleForecastBasis Basis { get; set; } = CycleForecastBasis.None;

    public bool IsHighVariability { get; set; }

    /// <summary>Days past the predicted start when it has already gone by; 0 otherwise.</summary>
    public int DaysLate { get; set; }

    public DateTime? LastEpisodeStart { get; set; }
}
