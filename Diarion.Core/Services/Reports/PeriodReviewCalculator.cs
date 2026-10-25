using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Models.Reports;

namespace Diarion.Services.Reports;

/// <summary>
/// Turns two period snapshots into one review. Pure: no clock, no services, no rounding of its own —
/// the snapshot already rounded, and the view decides how many digits to show.
/// </summary>
public static class PeriodReviewCalculator
{
    public static PeriodReview Compare(
        PeriodSnapshot current,
        PeriodSnapshot previous,
        StatsRange range,
        StatsRange previousRange)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        range = range.Normalized();
        previousRange = previousRange.Normalized();

        var previousHabits = previous.Habits.Good
            .Where(h => h.ScheduledDays > 0)
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var habits = current.Habits.Good
            .Where(h => h.ScheduledDays > 0)
            .OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(h =>
            {
                previousHabits.TryGetValue(h.Name, out var before);
                return new ReviewHabit(
                    h.Name,
                    h.CompletedDays,
                    h.ScheduledDays,
                    before?.CompletedDays,
                    before?.ScheduledDays);
            })
            .ToList();

        return new PeriodReview
        {
            Range = range,
            PreviousRange = previousRange,
            DayCount = range.Days,
            DaysWritten = new ReviewMetric(DaysWritten(current), DaysWritten(previous)),
            Mood = new ReviewMetric(MeanValence(current), MeanValence(previous)),
            SleepHours = new ReviewMetric(MeanSleepHours(current), MeanSleepHours(previous)),
            SleepQuality = new ReviewMetric(MeanSleepQuality(current), MeanSleepQuality(previous)),
            HabitCompletion = new ReviewMetric(HabitCompletion(current), HabitCompletion(previous)),
            TasksCompleted = new ReviewMetric(TasksCompleted(current), TasksCompleted(previous)),
            TasksTotal = new ReviewMetric(TasksTotal(current), TasksTotal(previous)),
            Expense = new ReviewMetric(Expense(current), Expense(previous)),
            Income = new ReviewMetric(Income(current), Income(previous)),
            TopEmotion = current.Mood.TopEmotion ?? string.Empty,
            Habits = habits
        };
    }

    /// <summary>
    /// Distinct dates with anything on them. Text days come from <see cref="PeriodSnapshot.Days"/>,
    /// which has a row for every calendar day whether or not it was written; mood and sleep are
    /// kept separately in the snapshot and are just as much a record of the day as a sentence is.
    /// </summary>
    private static double DaysWritten(PeriodSnapshot snapshot)
    {
        var dates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var day in snapshot.Days.Where(IsWritten))
        {
            dates.Add(day.Date);
        }

        foreach (var day in snapshot.Mood.Daily.Where(d => d.Valence.HasValue))
        {
            dates.Add(day.Date);
        }

        foreach (var night in snapshot.Sleep.Daily.Where(n => n.Hours.HasValue))
        {
            dates.Add(night.Date);
        }

        return dates.Count;
    }

    private static bool IsWritten(SnapshotDay day) =>
        day.Text is not null
        || day.Title is not null
        || day.Gratitude is not null
        || day.SoulFood is not null
        || day.Triggers is not null
        || day.SupportForOthers is not null
        || day.PromptAnswer is not null
        || day.SleepNotes is not null
        || day.IntimateLife is not null;

    private static double? MeanValence(PeriodSnapshot snapshot) =>
        Mean(snapshot.Mood.Daily.Select(d => d.Valence));

    private static double? MeanSleepHours(PeriodSnapshot snapshot) =>
        Mean(snapshot.Sleep.Daily.Select(n => n.Hours));

    private static double? MeanSleepQuality(PeriodSnapshot snapshot) =>
        Mean(snapshot.Sleep.Daily.Select(n => n.Quality));

    private static double? Mean(IEnumerable<double?> values)
    {
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : known.Average();
    }

    /// <summary>
    /// Completed over scheduled, summed across habits rather than averaged per habit: a daily habit
    /// and a once-a-week habit should weigh what they actually asked of the week.
    /// </summary>
    private static double? HabitCompletion(PeriodSnapshot snapshot)
    {
        var scheduled = snapshot.Habits.Good.Sum(h => h.ScheduledDays);
        if (scheduled == 0) return null;

        var completed = snapshot.Habits.Good.Sum(h => Math.Min(h.CompletedDays, h.ScheduledDays));
        return (double)completed / scheduled;
    }

    private static double? TasksCompleted(PeriodSnapshot snapshot) =>
        snapshot.Tasks.Total > 0 ? snapshot.Tasks.Completed : null;

    private static double? TasksTotal(PeriodSnapshot snapshot) =>
        snapshot.Tasks.Total > 0 ? snapshot.Tasks.Total : null;

    // Money is unknown, not zero, when the period recorded nothing in either direction. The
    // statistics layer cannot tell "spent nothing" from "did not use the finance module", and a
    // review that reports a drop to zero spending because someone stopped logging would be lying.
    private static double? Expense(PeriodSnapshot snapshot) =>
        HasMoney(snapshot) ? (double)snapshot.Finance.Expense : null;

    private static double? Income(PeriodSnapshot snapshot) =>
        HasMoney(snapshot) ? (double)snapshot.Finance.Income : null;

    private static bool HasMoney(PeriodSnapshot snapshot) =>
        snapshot.Finance.Expense != 0 || snapshot.Finance.Income != 0;
}
