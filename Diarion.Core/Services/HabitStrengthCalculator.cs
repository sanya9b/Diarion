using System;
using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Computes a recency-weighted "habit strength" (à la Loop Habits) and the current streak from a set of
/// completed dates. Strength is an exponential moving average over the habit's <b>scheduled</b> days
/// (1 = done, 0 = missed) with a 30-day-half-life; non-scheduled days are skipped so a Mon/Wed/Fri habit
/// isn't penalised for Tuesdays. Pure and deterministic — all dates are day-granularity.
/// </summary>
public static class HabitStrengthCalculator
{
    private const double HalfLifeDays = 30.0;
    private static readonly HabitSchedule DailySchedule = new() { Type = HabitScheduleType.Daily };

    public static double Strength(ISet<DateTime> completedDates, DateTime from, DateTime today)
        => Strength(completedDates, from, today, DailySchedule);

    /// <summary>Habit strength in [0, 100]. Applies the EMA on each scheduled day in [from, today].</summary>
    public static double Strength(ISet<DateTime> completedDates, DateTime from, DateTime today, HabitSchedule? schedule)
    {
        if (completedDates == null) return 0;

        var start = from.Date;
        var end = today.Date;
        if (end < start) return 0;

        var sched = schedule ?? DailySchedule;
        double alpha = 1.0 - Math.Pow(0.5, 1.0 / HalfLifeDays);
        double score = 0.0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!sched.IsScheduledOn(d)) continue;
            double value = completedDates.Contains(d.Date) ? 1.0 : 0.0;
            score += (value - score) * alpha;
        }

        return Math.Round(score * 100.0, MidpointRounding.AwayFromZero);
    }

    public static int CurrentStreak(ISet<DateTime> completedDates, DateTime today)
        => CurrentStreak(completedDates, today, DailySchedule);

    /// <summary>
    /// Consecutive completed <b>scheduled</b> days ending at the latest scheduled day on/before today.
    /// An unfinished today (its scheduled slot not yet done) doesn't break the streak; returns 0 otherwise.
    /// </summary>
    public static int CurrentStreak(ISet<DateTime> completedDates, DateTime today, HabitSchedule? schedule)
    {
        if (completedDates == null || completedDates.Count == 0) return 0;

        var sched = schedule ?? DailySchedule;

        var recent = MostRecentScheduledOnOrBefore(today.Date, sched);
        if (recent == null) return 0;
        var cursor = recent.Value;

        if (!completedDates.Contains(cursor))
        {
            // The latest scheduled day isn't done. If that's today, don't break the streak — start from
            // the previous scheduled day instead.
            if (cursor == today.Date)
            {
                var prev = MostRecentScheduledOnOrBefore(cursor.AddDays(-1), sched);
                if (prev == null) return 0;
                cursor = prev.Value;
            }

            if (!completedDates.Contains(cursor)) return 0;
        }

        int streak = 0;
        while (completedDates.Contains(cursor))
        {
            streak++;
            var prev = MostRecentScheduledOnOrBefore(cursor.AddDays(-1), sched);
            if (prev == null) break;
            cursor = prev.Value;
        }

        return streak;
    }

    // Walks back from `day` to the nearest scheduled day; bounded so a degenerate empty schedule terminates.
    private static DateTime? MostRecentScheduledOnOrBefore(DateTime day, HabitSchedule sched)
    {
        for (int i = 0; i < 3660; i++)
        {
            if (sched.IsScheduledOn(day)) return day;
            day = day.AddDays(-1);
        }
        return null;
    }
}
