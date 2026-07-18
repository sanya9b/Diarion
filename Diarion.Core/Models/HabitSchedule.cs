using System;
using System.Collections.Generic;

namespace Diarion.Models;

public enum HabitScheduleType
{
    /// <summary>Expected every day.</summary>
    Daily,
    /// <summary>Expected only on the listed weekdays.</summary>
    SpecificDays
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

    /// <summary>Whether the habit is expected on the given calendar day.</summary>
    public bool IsScheduledOn(DateTime date)
    {
        if (Type == HabitScheduleType.SpecificDays)
        {
            return DaysOfWeek != null && DaysOfWeek.Contains((int)date.DayOfWeek);
        }

        return true; // Daily
    }
}
