using System;
using System.Collections.Generic;

namespace Diarion.Models.Reports;

/// <summary>
/// One stretch of the diary set against the stretch of the same length just before it: the week
/// against the week before, the month against the month before.
/// </summary>
/// <remarks>
/// <para>
/// Built from two <see cref="Ai.Reports.PeriodSnapshot"/>s rather than from the statistics classes
/// directly, and that is the point of it. The snapshot is the one shape in the app that gathers
/// sleep, mood, habits, tasks, money and the text of every day into a single object, computed by
/// the same services the statistics screen draws from — so a number here can never disagree with the
/// chart two tabs over. It was written for the cloud report (spec 14) and sat without a screen; this
/// is that screen, done locally, with no model and no network.
/// </para>
/// <para>
/// Every metric is a pair, current and previous, and either half may be missing. Missing is
/// <c>null</c>, never zero: a week in which no sleep was logged is not a week of no sleep, and the
/// delta against it is not a drop. The view decides what to say about a missing half; this type only
/// refuses to invent one.
/// </para>
/// </remarks>
public sealed class PeriodReview
{
    public StatsRange Range { get; init; }

    public StatsRange PreviousRange { get; init; }

    /// <summary>Calendar days in <see cref="Range"/>; the denominator of <see cref="DaysWritten"/>.</summary>
    public int DayCount { get; init; }

    /// <summary>Days with anything at all in them: text, a mood, a night of sleep.</summary>
    public ReviewMetric DaysWritten { get; init; }

    /// <summary>Mean daily valence on the −2..+2 scale, over the days that have one.</summary>
    public ReviewMetric Mood { get; init; }

    /// <summary>Mean hours of sleep over the nights that were logged.</summary>
    public ReviewMetric SleepHours { get; init; }

    /// <summary>Mean star rating of sleep, out of <c>DiaryEntry.MaxRating</c>.</summary>
    public ReviewMetric SleepQuality { get; init; }

    /// <summary>Completed over scheduled across every good habit, 0..1. Missing when nothing was due.</summary>
    public ReviewMetric HabitCompletion { get; init; }

    /// <summary>Tasks ticked off. Missing when the period had no tasks at all.</summary>
    public ReviewMetric TasksCompleted { get; init; }

    /// <summary>Tasks the period had, done or not. Missing when none.</summary>
    public ReviewMetric TasksTotal { get; init; }

    /// <summary>Total spent. Missing when the period recorded no money in either direction.</summary>
    public ReviewMetric Expense { get; init; }

    /// <summary>Total earned, under the same rule as <see cref="Expense"/>.</summary>
    public ReviewMetric Income { get; init; }

    /// <summary>Name of the most frequent emotion in the period, empty when there was none.</summary>
    public string TopEmotion { get; init; } = string.Empty;

    /// <summary>One row per habit that was due at least once in the period, name order.</summary>
    public IReadOnlyList<ReviewHabit> Habits { get; init; } = Array.Empty<ReviewHabit>();

    /// <summary>
    /// True when the current period holds nothing worth a comparison. Zero habit completion counts
    /// as nothing: a person who set habits up and never ticked one has not started, and an empty
    /// state is a kinder first screen than six tiles of zeros.
    /// </summary>
    public bool IsEmpty =>
        (DaysWritten.Current ?? 0) == 0
        && (HabitCompletion.Current ?? 0) == 0
        && !TasksTotal.HasCurrent
        && !Expense.HasCurrent;
}

/// <summary>A number for this period and the same number for the one before, either of which may be unknown.</summary>
public readonly record struct ReviewMetric(double? Current, double? Previous)
{
    public static readonly ReviewMetric None = new(null, null);

    public bool HasCurrent => Current.HasValue;

    public bool HasPrevious => Previous.HasValue;

    /// <summary>Current minus previous, and only when both are known — a difference from nothing is not a number.</summary>
    public double? Delta => Current.HasValue && Previous.HasValue ? Current.Value - Previous.Value : null;
}

/// <summary>
/// One habit across the two periods. Previous counts are null when the habit did not exist or was
/// never due then, which is what tells "new this period" apart from "zero last period".
/// </summary>
public sealed record ReviewHabit(
    string Name,
    int CompletedDays,
    int ScheduledDays,
    int? PreviousCompletedDays,
    int? PreviousScheduledDays)
{
    public double Ratio => ScheduledDays == 0 ? 0 : (double)CompletedDays / ScheduledDays;

    public double? PreviousRatio =>
        PreviousScheduledDays is > 0 && PreviousCompletedDays.HasValue
            ? (double)PreviousCompletedDays.Value / PreviousScheduledDays.Value
            : null;
}
