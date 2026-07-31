using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Models;

/// <summary>
/// A task that repeats. Holds the template a <see cref="TodoItem"/> is stamped from plus the rule saying
/// which days it lands on — the same <see cref="RecurrenceRule"/> habits and planned transactions carry.
///
/// A separate entity rather than a flag on the row, for the reason <see cref="RecurringTransaction"/> is:
/// the series then has an identity of its own. The scheme this replaced had none, so it grouped instances
/// by <c>RepeatGroupId</c> and fell back to the task's own text when that was empty — which merged two
/// unrelated tasks that happened to read the same, and broke a series the moment one was renamed.
///
/// Unlike the finance rule there is no watermark and no backfill cap. Those exist because a ledger is
/// appended in time order, so "posted through day X" is a meaningful scalar. A planner is random access:
/// the user walks days in both directions, and a scalar cannot say "day D is dealt with but D+1 is not".
/// Occurrences are materialized lazily for one requested day and deduplicated by (rule, date), which is
/// idempotent without storing anything.
/// </summary>
public class RecurringTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // The template. Every field here is copied onto each materialized occurrence.
    public string TaskDescription { get; set; } = string.Empty;
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    public bool HasTime { get; set; }
    public TimeSpan TargetTime { get; set; }
    public bool HasReminder { get; set; }

    /// <summary>Which days the task lands on. Its <c>Anchor</c> starts the series and <c>EndDate</c> ends it.</summary>
    public RecurrenceRule Recurrence { get; set; } = new();

    /// <summary>
    /// Days the series deliberately does not land on, because the user deleted that one occurrence.
    /// Deleting a row cannot mean "delete the task" — the rule would simply produce it again the next time
    /// that day is opened — so it means "skip this day", and this is where that is remembered. It lives
    /// here rather than on <see cref="RecurrenceRule"/> because the rule is shared with habits and finance
    /// and has to stay a pure function of the calendar.
    ///
    /// Populate through <see cref="Skip"/>, never by adding to the list directly: the value needs the same
    /// calendar-day normalization the rule's own dates get, or LiteDB's UTC round trip moves it a day.
    /// </summary>
    public List<DateTime> SkippedDates { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public void Skip(DateTime date)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        if (!SkippedDates.Any(d => d.Date == day)) SkippedDates.Add(day);
    }

    public bool IsSkipped(DateTime date)
    {
        var day = date.Date;
        return SkippedDates.Any(d => d.Date == day);
    }
}
