using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diarion.Services;

public interface INotificationService
{
    void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime);
    void CancelTodoReminder(Guid todoId);

    /// <summary>
    /// Schedules a repeating reminder for a habit at <paramref name="timeOfDay"/>. When
    /// <paramref name="weekdays"/> is null or empty the reminder repeats daily; otherwise it repeats
    /// weekly on each listed weekday (<c>(int)DayOfWeek</c>). Replaces any existing reminder for the habit.
    /// </summary>
    void ScheduleHabitReminder(Guid habitId, string habitName, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays);
    void CancelHabitReminder(Guid habitId);

    /// <summary>
    /// A standing reminder for a repeating task, using the platform's own repeat so it fires whether or
    /// not the app has been opened. Null or empty <paramref name="weekdays"/> repeats daily; otherwise
    /// weekly on each listed weekday (<c>(int)DayOfWeek</c>). Replaces any existing one for the rule.
    /// </summary>
    void ScheduleRepeatingTaskReminder(Guid ruleId, string title, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays);

    /// <summary>
    /// One reminder per listed moment, for the rules no platform repeat can express — every N days, or a
    /// day of the month, or anything that stops on a date. Replaces any existing ones for the rule.
    /// </summary>
    void ScheduleTaskOccurrenceReminders(Guid ruleId, string title, IReadOnlyList<DateTime> moments);

    void CancelRepeatingTaskReminder(Guid ruleId);

    /// <summary>Schedules a repeating daily reminder to write the diary at the given time of day.</summary>
    void ScheduleDailyJournalReminder(TimeSpan timeOfDay);
    void CancelDailyJournalReminder();

    Task<bool> RequestPermissionsAsync();
}