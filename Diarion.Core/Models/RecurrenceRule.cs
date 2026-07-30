using System;
using System.Collections.Generic;

namespace Diarion.Models;

public enum RecurrenceKind
{
    /// <summary>Every day from the anchor onwards.</summary>
    Daily,
    /// <summary>Only on the listed weekdays.</summary>
    Weekly,
    /// <summary>Every Nth day, counted from the anchor.</summary>
    IntervalDays,
    /// <summary>One day per calendar month, clamped to the month's length.</summary>
    MonthlyByDay
}

/// <summary>
/// When something repeats, as a pure function of the calendar. Embedded as a sub-document on whatever
/// owns it rather than stored in its own collection.
/// </summary>
public class RecurrenceRule
{
    // A degenerate rule (one matching no day) or an unbounded window must not spin forever.
    private const int MaxWindowDays = 3660;

    public RecurrenceKind Kind { get; set; } = RecurrenceKind.Daily;

    /// <summary>Weekdays for <see cref="RecurrenceKind.Weekly"/>, as <c>(int)DayOfWeek</c> (0 = Sunday … 6 = Saturday).</summary>
    public List<int> DaysOfWeek { get; set; } = new();

    /// <summary>Step for <see cref="RecurrenceKind.IntervalDays"/>. Values below 1 read as 1.</summary>
    public int EveryN { get; set; } = 1;

    /// <summary>Target day for <see cref="RecurrenceKind.MonthlyByDay"/> (1–31), clamped down in shorter months.</summary>
    public int DayOfMonth { get; set; } = 1;

    private DateTime _anchor;

    /// <summary>
    /// First day the rule can fire, inclusive. <see cref="DateTime.MinValue"/> (the default) means no lower
    /// bound, which is what lets a rule deserialized from a document that never stored one behave as it did
    /// before it had an anchor.
    /// </summary>
    public DateTime Anchor { get => _anchor; set => _anchor = AsCalendarDay(value); }

    private DateTime? _endDate;

    /// <summary>Last day the rule can fire, inclusive. Null means open-ended.</summary>
    public DateTime? EndDate { get => _endDate; set => _endDate = value == null ? null : AsCalendarDay(value.Value); }

    /// <summary>
    /// Strips both the time and the kind, because these fields name a calendar day rather than an instant.
    /// The time has to go because IntervalDays counts <c>(date - Anchor).Days</c> and TimeSpan.Days truncates
    /// toward zero, so an anchor stored at 14:30 would shift the whole phase by a day. The kind has to go
    /// because LiteDB writes DateTime as UTC and reads it back as local: a value that arrives here as
    /// <see cref="DateTimeKind.Utc"/> midnight — which is what <c>DateTime.UtcNow.Date</c> produces, and
    /// UtcNow is this codebase's convention for timestamps — comes back as the previous calendar day
    /// anywhere west of Greenwich. Unspecified and Local both survive the round trip as the same day.
    /// </summary>
    private static DateTime AsCalendarDay(DateTime value)
        => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    /// <summary>Whether the rule fires on the given calendar day.</summary>
    public bool IsOccurrenceOn(DateTime date)
    {
        var day = date.Date;
        if (day < Anchor) return false;
        if (EndDate is DateTime end && day > end) return false;

        return Kind switch
        {
            RecurrenceKind.Daily => true,
            RecurrenceKind.Weekly => DaysOfWeek != null && DaysOfWeek.Contains((int)day.DayOfWeek),
            RecurrenceKind.IntervalDays => (day - Anchor).Days % Math.Max(1, EveryN) == 0,
            RecurrenceKind.MonthlyByDay => day.Day == EffectiveDayOfMonth(day),
            _ => false
        };
    }

    /// <summary>
    /// Every occurrence in [<paramref name="from"/>, <paramref name="to"/>], inclusive at both ends.
    /// Walks the range a day at a time and defers to <see cref="IsOccurrenceOn"/> rather than taking a
    /// per-kind shortcut: the two can then never disagree, which is the one bug class here that silently
    /// costs money. A window that would exceed <see cref="MaxWindowDays"/> is cut short.
    /// </summary>
    public IEnumerable<DateTime> Enumerate(DateTime from, DateTime to)
    {
        var cursor = from.Date;
        if (cursor < Anchor) cursor = Anchor;

        var end = to.Date;
        if (EndDate is DateTime stop && end > stop) end = stop;

        for (var guard = 0; guard < MaxWindowDays && cursor <= end; guard++, cursor = cursor.AddDays(1))
        {
            if (IsOccurrenceOn(cursor)) yield return cursor;
        }
    }

    /// <summary>
    /// The day of <paramref name="date"/>'s month this rule actually lands on. A rule set to the 31st fires
    /// on the 30th of April and the 28th of February — clamping down rather than skipping the month, because
    /// the caller is a bill or a salary and a month that silently produces no rent is a hole in the budget.
    /// (RFC 5545's BYMONTHDAY skips instead, which is right for calendars and wrong for money.)
    /// </summary>
    private int EffectiveDayOfMonth(DateTime date)
        => Math.Min(Math.Clamp(DayOfMonth, 1, 31), DateTime.DaysInMonth(date.Year, date.Month));
}
