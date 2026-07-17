using System;
using System.Threading.Tasks;

namespace Diarion.Services;

public interface INotificationService
{
    void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime);
    void CancelTodoReminder(Guid todoId);

    /// <summary>Schedules a repeating daily reminder to write the diary at the given time of day.</summary>
    void ScheduleDailyJournalReminder(TimeSpan timeOfDay);
    void CancelDailyJournalReminder();

    Task<bool> RequestPermissionsAsync();
}