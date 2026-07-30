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
    private static readonly RecurrenceRule DailySchedule = new() { Kind = RecurrenceKind.Daily };

    public static double Strength(ISet<DateTime> completedDates, DateTime from, DateTime today)
        => Strength(completedDates, from, today, DailySchedule);

    /// <summary>Habit strength in [0, 100]. Applies the EMA on each scheduled day in [from, today].</summary>
    public static double Strength(
        ISet<DateTime> completedDates,
        DateTime from,
        DateTime today,
        RecurrenceRule? schedule,
        CompletionTarget? target = null)
    {
        if (completedDates == null) return 0;

        var start = from.Date;
        var end = today.Date;
        if (end < start) return 0;

        var sched = schedule ?? DailySchedule;

        if (target != null)
        {
            return WeeklyStrength(completedDates, start, end, Math.Max(1, target.TimesPerWeek));
        }

        double alpha = 1.0 - Math.Pow(0.5, 1.0 / HalfLifeDays);
        double score = 0.0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!sched.IsOccurrenceOn(d)) continue;
            double value = completedDates.Contains(d.Date) ? 1.0 : 0.0;
            score += (value - score) * alpha;
        }

        return Math.Round(score * 100.0, MidpointRounding.AwayFromZero);
    }

    public static int CurrentStreak(ISet<DateTime> completedDates, DateTime today)
        => CurrentStreak(completedDates, today, DailySchedule, target: null, graceDays: 0);

    public static int CurrentStreak(ISet<DateTime> completedDates, DateTime today, RecurrenceRule? schedule)
        => CurrentStreak(completedDates, today, schedule, target: null, graceDays: 0);

    /// <summary>
    /// Consecutive completed <b>scheduled</b> days ending at the latest scheduled day on/before today.
    /// An unfinished today (its scheduled slot not yet done) doesn't break the streak. Up to
    /// <paramref name="graceDays"/> missed scheduled days are forgiven across the run; days the habit was
    /// never due on cost nothing. A quota habit counts whole weeks, so grace does not apply to it.
    /// </summary>
    public static int CurrentStreak(
        ISet<DateTime> completedDates,
        DateTime today,
        RecurrenceRule? schedule,
        CompletionTarget? target,
        int graceDays)
    {
        if (completedDates == null || completedDates.Count == 0) return 0;

        var sched = schedule ?? DailySchedule;

        if (target != null)
        {
            return WeeklyStreak(completedDates, today.Date, Math.Max(1, target.TimesPerWeek));
        }

        return StreakWalker.Walk(completedDates, today, graceDays, sched.IsOccurrenceOn).Length;
    }

    // --- TimesPerWeek (weekly granularity) ---

    private static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7)); // Monday

    private static int CountInWeek(ISet<DateTime> completed, DateTime start, DateTime end)
    {
        int c = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (completed.Contains(d.Date)) c++;
        }
        return c;
    }

    // Weekly EMA over completed weeks (weekValue = min(count/target, 1)); the in-progress week is only
    // applied when the target is already met, so a fresh week never crashes the score mid-week.
    private static double WeeklyStrength(ISet<DateTime> completed, DateTime from, DateTime today, int target)
    {
        double alpha = 1.0 - Math.Pow(0.5, 7.0 / HalfLifeDays); // 30-day half-life expressed per week
        double score = 0.0;

        var currentWeek = WeekStart(today);
        for (var ws = WeekStart(from); ws < currentWeek; ws = ws.AddDays(7))
        {
            int c = CountInWeek(completed, ws, ws.AddDays(6));
            double v = Math.Min(1.0, (double)c / target);
            score += (v - score) * alpha;
        }

        int currentCount = CountInWeek(completed, currentWeek, today);
        if (currentCount >= target)
        {
            score += (1.0 - score) * alpha;
        }

        return Math.Round(score * 100.0, MidpointRounding.AwayFromZero);
    }

    // Consecutive weeks meeting the target, ending at the current week (if met) or the last completed week.
    private static int WeeklyStreak(ISet<DateTime> completed, DateTime today, int target)
    {
        var ws = WeekStart(today);
        if (CountInWeek(completed, ws, today) < target)
        {
            ws = ws.AddDays(-7); // current week not met yet — don't break the streak
        }

        int streak = 0;
        for (int guard = 0; guard < 100000; guard++)
        {
            if (CountInWeek(completed, ws, ws.AddDays(6)) < target) break;
            streak++;
            ws = ws.AddDays(-7);
        }
        return streak;
    }
}
