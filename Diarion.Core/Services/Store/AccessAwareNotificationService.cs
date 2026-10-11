namespace Diarion.Services.Store;

/// <summary>
/// Wraps the platform notification service so that no daily or habit reminder is scheduled while
/// the diary is read-only (spec 15, R1.6) — the settings toggle still saves, and
/// <see cref="ReminderAccessCoordinator"/> schedules it the moment access returns. Cancelling,
/// task reminders and permission requests pass straight through.
/// </summary>
public sealed class AccessAwareNotificationService : INotificationService
{
    private readonly INotificationService _inner;
    private readonly IAccessGuard _guard;

    public AccessAwareNotificationService(INotificationService inner, IAccessGuard guard)
    {
        _inner = inner;
        _guard = guard;
    }

    public void ScheduleDailyJournalReminder(TimeSpan timeOfDay)
    {
        if (_guard.CanWrite) _inner.ScheduleDailyJournalReminder(timeOfDay);
    }

    public void ScheduleHabitReminder(Guid habitId, string habitName, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays)
    {
        if (_guard.CanWrite) _inner.ScheduleHabitReminder(habitId, habitName, timeOfDay, weekdays);
    }

    public void CancelDailyJournalReminder() => _inner.CancelDailyJournalReminder();
    public void CancelHabitReminder(Guid habitId) => _inner.CancelHabitReminder(habitId);

    public void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime) =>
        _inner.ScheduleTodoReminder(todoId, title, description, targetTime);

    public void CancelTodoReminder(Guid todoId) => _inner.CancelTodoReminder(todoId);

    public void ScheduleRepeatingTaskReminder(Guid ruleId, string title, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays) =>
        _inner.ScheduleRepeatingTaskReminder(ruleId, title, timeOfDay, weekdays);

    public void ScheduleTaskOccurrenceReminders(Guid ruleId, string title, IReadOnlyList<DateTime> moments) =>
        _inner.ScheduleTaskOccurrenceReminders(ruleId, title, moments);

    public void CancelRepeatingTaskReminder(Guid ruleId) => _inner.CancelRepeatingTaskReminder(ruleId);

    public Task<bool> RequestPermissionsAsync() => _inner.RequestPermissionsAsync();
}
