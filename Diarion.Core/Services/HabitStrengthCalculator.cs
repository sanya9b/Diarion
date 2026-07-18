using System;
using System.Collections.Generic;

namespace Diarion.Services;

/// <summary>
/// Computes a recency-weighted "habit strength" (à la Loop Habits) and the current streak from a set of
/// completed dates. Strength is an exponential moving average over daily values (1 = done, 0 = missed)
/// with a 30-day half-life, so recent behaviour dominates and gaps decay the score over time.
/// Pure and deterministic — all dates are treated as day-granularity.
/// </summary>
public static class HabitStrengthCalculator
{
    private const double HalfLifeDays = 30.0;

    /// <summary>Habit strength in [0, 100]. Iterates each day in [from, today] applying the EMA.</summary>
    public static double Strength(ISet<DateTime> completedDates, DateTime from, DateTime today)
    {
        if (completedDates == null) return 0;

        var start = from.Date;
        var end = today.Date;
        if (end < start) return 0;

        double alpha = 1.0 - Math.Pow(0.5, 1.0 / HalfLifeDays);
        double score = 0.0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            double value = completedDates.Contains(d.Date) ? 1.0 : 0.0;
            score += (value - score) * alpha;
        }

        return Math.Round(score * 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Consecutive completed days ending today. If today isn't done yet the streak is measured up to
    /// yesterday (an unfinished today doesn't break the streak); returns 0 when neither is completed.
    /// </summary>
    public static int CurrentStreak(ISet<DateTime> completedDates, DateTime today)
    {
        if (completedDates == null || completedDates.Count == 0) return 0;

        var cursor = today.Date;
        if (!completedDates.Contains(cursor))
        {
            cursor = cursor.AddDays(-1);
            if (!completedDates.Contains(cursor)) return 0;
        }

        int streak = 0;
        while (completedDates.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }
}
