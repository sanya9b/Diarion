using System;

namespace Diarion.Models;

/// <summary>
/// The window every statistics query runs over. Both ends are inclusive calendar days, never instants —
/// the whole screen speaks in days, and an end-of-day timestamp would only invite off-by-one bugs.
///
/// This exists because the screen used to pass a single <c>int days</c> around, which can only ever mean
/// "the N days ending today". The data layer always accepted two dates; the statistics layer was the one
/// place that collapsed them into one number, and so the only thing you could not ask for was a period
/// that ended in the past.
/// </summary>
public readonly record struct StatsRange(DateTime Start, DateTime End)
{
    /// <summary>Length in calendar days, both ends counted: a range of one day is 1, not 0.</summary>
    public int Days => (End.Date - Start.Date).Days + 1;

    /// <summary>The rolling window the period chips stand for: N days ending today, today included.</summary>
    public static StatsRange LastDays(int days)
    {
        var today = DateTime.Today;
        return new StatsRange(today.AddDays(-(Math.Max(1, days) - 1)), today);
    }

    /// <summary>From the first of the current month to today — what the screen opens on.</summary>
    public static StatsRange MonthToDate()
    {
        var today = DateTime.Today;
        return new StatsRange(new DateTime(today.Year, today.Month, 1), today);
    }

    /// <summary>
    /// Times of day dropped and the ends put in order. Every consumer calls this rather than trusting the
    /// caller: an inverted window silently returns nothing, which reads on screen as "you logged nothing".
    /// </summary>
    public StatsRange Normalized()
    {
        var from = Start.Date;
        var to = End.Date;
        return to < from ? new StatsRange(to, from) : new StatsRange(from, to);
    }
}
