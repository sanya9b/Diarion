using System;
using System.Collections.Generic;

namespace Diarion.Models;

/// <summary>One completed cycle: when it began and how many days passed before the next period started.</summary>
public class CycleLengthPoint
{
    public DateTime Start { get; init; }
    public int Days { get; init; }

    /// <summary>
    /// False for intervals outside the plausible band, which are almost always a stretch of forgotten
    /// logging rather than a real cycle. Charted anyway — the gap is worth seeing — but kept out of the
    /// averages, for the same reason <see cref="Diarion.Services.CycleForecastCalculator"/> discards them.
    /// </summary>
    public bool IsPlausible { get; init; }
}

/// <summary>How often one symptom was logged over the whole log.</summary>
public class CycleSymptomCount
{
    /// <summary>A <see cref="CycleSymptoms"/> key; resolve to a label through the resx, never store text.</summary>
    public string Key { get; init; } = string.Empty;

    public int Count { get; init; }
}

/// <summary>
/// The cycle as statistics rather than as a forecast: every recorded cycle length, the spread across
/// them, how long periods tend to run, and which symptoms come up most.
/// </summary>
public class CycleStatistics
{
    public static readonly CycleStatistics Empty = new();

    /// <summary>Most recent last, capped for the chart. Includes implausible intervals.</summary>
    public IReadOnlyList<CycleLengthPoint> Cycles { get; init; } = Array.Empty<CycleLengthPoint>();

    /// <summary>Null until at least one plausible cycle has been recorded.</summary>
    public int? AverageCycleLength { get; init; }
    public int? ShortestCycle { get; init; }
    public int? LongestCycle { get; init; }

    /// <summary>Mean marked days per period, over periods that have finished.</summary>
    public double? AveragePeriodLength { get; init; }

    /// <summary>Plausible cycles behind the averages — the base the numbers stand on.</summary>
    public int RecordedCycleCount { get; init; }

    /// <summary>Intervals left out of the averages as implausible; shown so the base is never silent.</summary>
    public int DiscardedCycleCount { get; init; }

    public IReadOnlyList<CycleSymptomCount> Symptoms { get; init; } = Array.Empty<CycleSymptomCount>();

    public IReadOnlyCollection<DateTime> MarkedDates { get; init; } = Array.Empty<DateTime>();

    public bool IsEmpty => MarkedDates.Count == 0;
}
