using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Predicts the next period from the days the user actually logged, rather than from a fixed cycle
/// length ticking off one anchor date. Consecutive marked days form an episode; the gaps between
/// episode starts are the cycles; their rolling mean is the prediction and their spread is how far it
/// can be trusted.
///
/// Two things it deliberately refuses to do. It never wraps the cycle day, so a late period reads as
/// "day 41" instead of quietly restarting at day 13 — the wrap is exactly the information someone is
/// looking for. And it discards intervals outside a plausible band, because a month of forgotten
/// logging otherwise enters the average as a sixty-day cycle and drags every later prediction with it.
///
/// Pure and deterministic — all dates are day-granularity and "today" is passed in.
/// </summary>
public static class CycleForecastCalculator
{
    /// <summary>One forgotten day inside a period still counts as the same period; two do not.</summary>
    public const int MaxEpisodeGapDays = 2;

    public const int MinPlausibleCycleDays = 21;
    public const int MaxPlausibleCycleDays = 45;

    /// <summary>How many recent cycles the average looks at, so an old irregular stretch stops counting.</summary>
    public const int HistoryWindow = 6;

    /// <summary>The range shown off a single measured cycle, where there is no spread to measure yet.</summary>
    public const int DefaultUncertaintyDays = 2;

    public const int MaxUncertaintyDays = 7;

    /// <summary>Standard deviation at or above which the estimate is called out as rough.</summary>
    public const double HighVariabilitySdDays = 7.0;

    private const int OvulationBeforeNextStartDays = 14;
    private const int FertileWindowBeforeOvulationDays = 5;
    private const int FertileWindowAfterOvulationDays = 1;

    public static CycleHistory BuildHistory(IReadOnlyList<DateTime>? markedDates)
    {
        if (markedDates is not { Count: > 0 }) return CycleHistory.Empty;

        var days = markedDates.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();

        var episodes = new List<CycleEpisode>();
        var start = days[0];
        var previous = days[0];
        var length = 1;

        foreach (var day in days.Skip(1))
        {
            if ((day - previous).Days <= MaxEpisodeGapDays)
            {
                length++;
            }
            else
            {
                episodes.Add(new CycleEpisode { Start = start, End = previous, Length = length });
                start = day;
                length = 1;
            }

            previous = day;
        }

        episodes.Add(new CycleEpisode { Start = start, End = previous, Length = length });

        var intervals = new List<int>();
        for (int i = 1; i < episodes.Count; i++)
        {
            intervals.Add((episodes[i].Start - episodes[i - 1].Start).Days);
        }

        // Window over intervals rather than over survivors, so the discard count below describes the
        // same slice the average is taken from. Windowing survivors first would let old gaps keep
        // voting against a run of recent, clean cycles.
        var recent = intervals.Skip(Math.Max(0, intervals.Count - HistoryWindow)).ToList();
        var usable = recent.Where(i => i is >= MinPlausibleCycleDays and <= MaxPlausibleCycleDays).ToList();

        return new CycleHistory
        {
            Episodes = episodes,
            UsableIntervals = usable,
            DiscardedIntervals = recent.Count - usable.Count,
            MarkedDates = new HashSet<DateTime>(days)
        };
    }

    public static CycleForecast Describe(CycleHistory? history, UserProfile? profile, DateTime date, DateTime today)
    {
        var day = date.Date;
        var forecast = new CycleForecast();

        if (history is null || history.Episodes.Count == 0) return forecast;

        forecast.IsAvailable = true;
        forecast.IsPeriodDay = history.IsMarked(day);

        var lastStart = history.Episodes[^1].Start;
        forecast.LastEpisodeStart = lastStart;

        // Counted from the last episode start and never wrapped: a cycle that has run long should say so.
        forecast.CycleDay = day >= lastStart ? (day - lastStart).Days + 1 : 0;

        var (mean, uncertainty, basis, sd) = Resolve(history, profile);
        forecast.Basis = basis;
        forecast.AverageCycleLength = mean;
        forecast.UncertaintyDays = uncertainty;
        forecast.RecordedCycleCount = history.UsableIntervals.Count;
        forecast.IsHighVariability = basis == CycleForecastBasis.Averaged && sd >= HighVariabilitySdDays;

        var predictedStart = lastStart.AddDays((int)Math.Round(mean, MidpointRounding.AwayFromZero));
        forecast.PredictedNextStart = predictedStart;

        // Do not roll the prediction forward once it has passed. Pretending the next cycle already began
        // would hide the one fact worth surfacing.
        if (predictedStart < today.Date) forecast.DaysLate = (today.Date - predictedStart).Days;

        var periodLength = profile?.GetNormalizedPeriodLength() ?? UserProfile.DefaultPeriodLength;
        if (!forecast.IsPeriodDay && day >= predictedStart && day < predictedStart.AddDays(periodLength))
        {
            forecast.IsPredictedPeriodDay = true;
        }

        // Anchored to the predicted start rather than to a nominal cycle length: ovulation tracks the
        // end of the cycle, not its beginning.
        var ovulation = predictedStart.AddDays(-OvulationBeforeNextStartDays);
        forecast.IsFertileWindowEstimate =
            day >= ovulation.AddDays(-FertileWindowBeforeOvulationDays) &&
            day <= ovulation.AddDays(FertileWindowAfterOvulationDays);

        return forecast;
    }

    private static (double Mean, int Uncertainty, CycleForecastBasis Basis, double Sd) Resolve(
        CycleHistory history, UserProfile? profile)
    {
        var fallback = profile?.GetNormalizedCycleLength() ?? UserProfile.DefaultCycleLength;
        var intervals = history.UsableIntervals;

        // More discards than survivors means the logging has holes; one lucky interval among them would
        // produce a confidently wrong number, so fall back to the setting and say so.
        var unreliable = intervals.Count == 0 || history.DiscardedIntervals > intervals.Count;
        if (unreliable) return (fallback, 0, CycleForecastBasis.ProfileDefault, 0);

        if (intervals.Count == 1)
        {
            return (intervals[0], DefaultUncertaintyDays, CycleForecastBasis.SingleCycle, 0);
        }

        var mean = intervals.Average();

        // Population standard deviation: the window is the whole population we care about, and the
        // sample correction would blow the range up absurdly at two intervals.
        var variance = intervals.Sum(i => (i - mean) * (i - mean)) / intervals.Count;
        var sd = Math.Sqrt(variance);

        var uncertainty = Math.Clamp((int)Math.Round(sd, MidpointRounding.AwayFromZero), 1, MaxUncertaintyDays);
        return (mean, uncertainty, CycleForecastBasis.Averaged, sd);
    }
}
