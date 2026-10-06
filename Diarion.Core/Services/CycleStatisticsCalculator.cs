using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Turns the logged period days into history rather than into a prediction. Where
/// <see cref="CycleForecastCalculator"/> looks at the last six cycles to guess the next one, this looks
/// at all of them to describe what has actually happened.
///
/// Pure and deterministic; "today" is passed in.
/// </summary>
public static class CycleStatisticsCalculator
{
    /// <summary>Bars past this stop being readable on a phone, so the chart keeps the most recent ones.</summary>
    public const int MaxChartedCycles = 12;

    public static CycleStatistics Compute(CycleHistory? history, IReadOnlyList<CycleLog>? logs, DateTime today)
    {
        var episodes = history?.Episodes ?? Array.Empty<CycleEpisode>();
        var marked = history?.MarkedDates ?? Array.Empty<DateTime>();

        var allCycles = new List<CycleLengthPoint>();
        for (int i = 1; i < episodes.Count; i++)
        {
            int days = (episodes[i].Start - episodes[i - 1].Start).Days;
            allCycles.Add(new CycleLengthPoint
            {
                Start = episodes[i - 1].Start,
                Days = days,
                IsPlausible = days is >= CycleForecastCalculator.MinPlausibleCycleDays
                                   and <= CycleForecastCalculator.MaxPlausibleCycleDays
            });
        }

        var plausible = allCycles.Where(c => c.IsPlausible).Select(c => c.Days).ToList();

        return new CycleStatistics
        {
            Cycles = allCycles.Skip(Math.Max(0, allCycles.Count - MaxChartedCycles)).ToList(),
            AverageCycleLength = plausible.Count > 0 ? (int)Math.Round(plausible.Average()) : null,
            ShortestCycle = plausible.Count > 0 ? plausible.Min() : null,
            LongestCycle = plausible.Count > 0 ? plausible.Max() : null,
            AveragePeriodLength = AveragePeriodLength(episodes, today),
            RecordedCycleCount = plausible.Count,
            DiscardedCycleCount = allCycles.Count - plausible.Count,
            Symptoms = CountSymptoms(logs),
            MarkedDates = marked
        };
    }

    /// <summary>
    /// Mean marked days per period. A period that may still be running is left out: it has only been
    /// logged as far as today, and counting it would pull the average down every single month.
    /// </summary>
    private static double? AveragePeriodLength(IReadOnlyList<CycleEpisode> episodes, DateTime today)
    {
        var cutoff = today.Date.AddDays(-CycleForecastCalculator.MaxEpisodeGapDays);
        var finished = episodes.Where(e => e.End.Date < cutoff).ToList();

        return finished.Count > 0 ? Math.Round(finished.Average(e => e.Length), 1) : null;
    }

    private static IReadOnlyList<CycleSymptomCount> CountSymptoms(IReadOnlyList<CycleLog>? logs)
    {
        if (logs is not { Count: > 0 }) return Array.Empty<CycleSymptomCount>();

        var counts = new Dictionary<string, int>();
        foreach (var log in logs)
        {
            foreach (var key in log.Symptoms ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }

        // Ties broken by key so the order is stable between loads rather than dictionary-dependent.
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CycleSymptomCount { Key = kv.Key, Count = kv.Value })
            .ToList();
    }
}
