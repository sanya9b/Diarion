using System;
using System.Collections.Generic;

namespace Diarion.Models;

public enum HabitScheduleType
{
    /// <summary>Expected every day.</summary>
    Daily,
    /// <summary>Expected only on the listed weekdays.</summary>
    SpecificDays,
    /// <summary>A weekly target of N completions on any days.</summary>
    TimesPerWeek
}

/// <summary>
/// When a good habit is expected. Drives which days it appears on in the daily tracker and which days
/// count toward strength/streak. Embedded on <see cref="HabitDefinition"/>; legacy habits (no stored
/// schedule) default to <see cref="HabitScheduleType.Daily"/>.
/// </summary>
public class HabitSchedule
{
    public HabitScheduleType Type { get; set; } = HabitScheduleType.Daily;

    /// <summary>Weekdays this habit is expected on, as <c>(int)DayOfWeek</c> (0 = Sunday … 6 = Saturday).</summary>
    public List<int> DaysOfWeek { get; set; } = new();

    /// <summary>Weekly target for <see cref="HabitScheduleType.TimesPerWeek"/> (1–7).</summary>
    public int TimesPerWeek { get; set; } = 3;

    /// <summary>
    /// Whether the habit is expected on the given calendar day. Daily and TimesPerWeek are open on
    /// every day (the latter has a weekly target rather than fixed days); SpecificDays gates by weekday.
    /// </summary>
    public bool IsScheduledOn(DateTime date)
    {
        if (Type == HabitScheduleType.SpecificDays)
        {
            return DaysOfWeek != null && DaysOfWeek.Contains((int)date.DayOfWeek);
        }

        return true; // Daily or TimesPerWeek — any day
    }
}
