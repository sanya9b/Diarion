using System;
using Diarion.Models;
using Diarion.Models.Ai.Reports;

namespace Diarion.Services.Ai.Reports;

/// <summary>
/// Which calendar window a report covers, and whether that window is over.
/// </summary>
/// <remarks>
/// Pure date arithmetic with the clock passed in, so the awkward days — the 29th of February, the
/// week that straddles New Year, the quarter that starts mid-week — are testable without waiting
/// for them to come round.
/// </remarks>
public static class PeriodBoundaries
{
    /// <summary>
    /// The period of <paramref name="kind"/> that <paramref name="day"/> falls inside, both ends
    /// inclusive calendar days.
    /// </summary>
    public static StatsRange Containing(PeriodKind kind, DateTime day)
    {
        var date = day.Date;

        return kind switch
        {
            PeriodKind.Week => WeekOf(date),
            PeriodKind.Month => MonthOf(date),
            PeriodKind.Quarter => QuarterOf(date),
            PeriodKind.Year => new StatsRange(new DateTime(date.Year, 1, 1), new DateTime(date.Year, 12, 31)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    /// <summary>
    /// The most recent period of <paramref name="kind"/> that has already ended. Never the current
    /// one: a report on four days of a week draws conclusions the remaining three would contradict,
    /// and it would then have to be silently replaced, which reads as the app changing its mind.
    /// </summary>
    public static StatsRange LastClosed(PeriodKind kind, DateTime today)
    {
        // One day before this period started is, by definition, the last day of the previous one.
        return Containing(kind, Containing(kind, today).Start.AddDays(-1));
    }

    /// <summary>Whether <paramref name="range"/> ended before today began.</summary>
    public static bool IsClosed(StatsRange range, DateTime today) => range.Normalized().End < today.Date;

    /// <summary>
    /// Monday-based, matching <c>HabitStrengthCalculator</c> and the rest of the app. Not a locale
    /// lookup: a week that starts on Sunday for one screen and Monday for another would report two
    /// different numbers for the same seven days.
    /// </summary>
    private static StatsRange WeekOf(DateTime date)
    {
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return new StatsRange(monday, monday.AddDays(6));
    }

    private static StatsRange MonthOf(DateTime date)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        return new StatsRange(first, first.AddMonths(1).AddDays(-1));
    }

    private static StatsRange QuarterOf(DateTime date)
    {
        var first = new DateTime(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1);
        return new StatsRange(first, first.AddMonths(3).AddDays(-1));
    }
}
