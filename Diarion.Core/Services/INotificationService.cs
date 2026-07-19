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

    /// <summary>Schedules a repeating daily reminder to write the diary at the given time of day.</summary>
    void ScheduleDailyJournalReminder(TimeSpan timeOfDay);
    void CancelDailyJournalReminder();

    Task<bool> RequestPermissionsAsync();
}