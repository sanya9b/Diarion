using Diarion.Models;

namespace Diarion.Services.Store;

/// <summary>
/// Spec 15, R1.6: a nudge to write is spam while writing is closed. When the diary turns read-only
/// the daily journal reminder and every habit and tracker reminder are cancelled; when access
/// returns they are scheduled again from the same settings, which never changed. Task reminders
/// are left alone — they are about the user's own errands, not a prompt to use the app.
/// </summary>
public sealed class ReminderAccessCoordinator : IDisposable
{
    internal const string PausedKey = "access.remindersPaused";

    private readonly IEntitlementService _entitlements;
    private readonly INotificationService _notifications;
    private readonly IProfileService _profiles;
    private readonly IHabitService _habits;
    private readonly IEntitlementStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ReminderAccessCoordinator(
        IEntitlementService entitlements,
        INotificationService notifications,
        IProfileService profiles,
        IHabitService habits,
        IEntitlementStore store)
    {
        _entitlements = entitlements;
        _notifications = notifications;
        _profiles = profiles;
        _habits = habits;
        _store = store;
    }

    /// <summary>
    /// Applies the state the app started in, then follows every change. The paused flag is
    /// persisted, so reminders cancelled in one session come back in a later one even if access
    /// returned while the app was closed.
    /// </summary>
    public Task StartAsync()
    {
        _entitlements.Changed -= OnChanged;
        _entitlements.Changed += OnChanged;
        return ApplyAsync(_entitlements.Current);
    }

    public void Dispose()
    {
        _entitlements.Changed -= OnChanged;
        _gate.Dispose();
    }

    private void OnChanged(AccessState state) => _ = ApplyAsync(state);

    public async Task ApplyAsync(AccessState state)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var paused = _store.Get(PausedKey) == "1";
            if (state.IsReadOnly && !paused)
            {
                await PauseAsync().ConfigureAwait(false);
                _store.Set(PausedKey, "1");
            }
            else if (state.CanWrite && paused)
            {
                await ResumeAsync().ConfigureAwait(false);
                _store.Remove(PausedKey);
            }
        }
        catch (Exception ex)
        {
            // A reminder that could not be moved is not worth a crash; the next change retries.
            System.Diagnostics.Debug.WriteLine($"Reminder pause/resume failed: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PauseAsync()
    {
        _notifications.CancelDailyJournalReminder();
        foreach (var habit in await _habits.GetLiveHabitDefinitionsAsync().ConfigureAwait(false))
            _notifications.CancelHabitReminder(habit.Id);
        foreach (var tracker in await _habits.GetHarmfulHabitTrackersAsync().ConfigureAwait(false))
            _notifications.CancelHabitReminder(tracker.Id);
    }

    private async Task ResumeAsync()
    {
        var profile = await _profiles.GetUserProfileAsync().ConfigureAwait(false);
        if (profile.IsDailyReminderEnabled)
            _notifications.ScheduleDailyJournalReminder(profile.DailyReminderTime);

        foreach (var habit in await _habits.GetLiveHabitDefinitionsAsync().ConfigureAwait(false))
        {
            if (habit.ReminderTime is not { } time) continue;
            // Same rule as HabitEditorViewModel: weekly habits remind on their days, the rest daily.
            var weekdays = habit.Schedule?.Kind == RecurrenceKind.Weekly ? habit.Schedule.DaysOfWeek : null;
            _notifications.ScheduleHabitReminder(habit.Id, habit.Name, time, weekdays);
        }

        foreach (var tracker in await _habits.GetHarmfulHabitTrackersAsync().ConfigureAwait(false))
        {
            if (tracker.ReminderTime is { } time)
                _notifications.ScheduleHabitReminder(tracker.Id, tracker.HarmfulHabitName, time, null);
        }
    }
}
