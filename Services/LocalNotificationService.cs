using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace Diarion.Services;

public class LocalNotificationService : INotificationService
{
    private readonly ISpeechService _speech;

    public LocalNotificationService(ISpeechService speech) => _speech = speech;

    /// <summary>
    /// Every reminder in the app is scheduled inexactly but doze-aware.
    /// <para>
    /// Exact alarms would be a little more punctual, and they cost two permissions to get there.
    /// <c>USE_EXACT_ALARM</c> is restricted by Google Play to alarm clocks and calendars, which a diary
    /// is not; <c>SCHEDULE_EXACT_ALARM</c> stopped being granted automatically on Android 14, so from
    /// there on an exact schedule silently degrades unless the user is sent into system settings to
    /// turn it on. A reminder that quietly stops arriving is the worst failure this feature has, so
    /// neither is worth a few minutes of precision on an evening journalling nudge.
    /// </para>
    /// <para>
    /// The cost is real and belongs on the record: in deep doze Android may hold an inexact alarm for
    /// up to roughly ten minutes. If that ever matters for a timed task, the fix is to ask
    /// <c>IAndroidNotificationService.CanScheduleExactNotifications</c> and pick
    /// <see cref="AndroidScheduleMode.ExactAllowWhileIdle"/> when it is already permitted — one line
    /// here, because every reminder goes through <see cref="At"/>.
    /// </para>
    /// </summary>
    private const AndroidScheduleMode ReminderScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle;

    /// <summary>
    /// The one place a reminder's timing is described. Shared so the schedule mode cannot drift
    /// between the seven call sites that used to spell this out individually.
    /// </summary>
    private static NotificationRequestSchedule At(DateTime notifyTime, NotificationRepeat? repeat = null)
    {
        var schedule = new NotificationRequestSchedule
        {
            NotifyTime = notifyTime,
            Android = new AndroidScheduleOptions { ScheduleMode = ReminderScheduleMode }
        };

        if (repeat.HasValue)
        {
            schedule.RepeatType = repeat.Value;
        }

        return schedule;
    }

    public void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime, bool spoken = false)
    {
#if ANDROID || IOS || MACCATALYST
        CancelTodoReminder(todoId);

        if (targetTime > DateTime.Now)
        {
            var request = new NotificationRequest
            {
                NotificationId = todoId.GetHashCode(),
                Title = title,
                Description = description,
                Schedule = At(targetTime)
            };

            if (spoken) ShowSpoken(todoId, "todo", description, new[] { request });
            else LocalNotificationCenter.Current.Show(request);
        }
#endif
    }

    public void CancelTodoReminder(Guid todoId)
    {
#if ANDROID || IOS || MACCATALYST
        LocalNotificationCenter.Current.Cancel(todoId.GetHashCode());
        ForgetSpoken(todoId, "todo");
#endif
    }

#if ANDROID || IOS || MACCATALYST
    // -- Spoken reminders (spec 19) --------------------------------------------------------------
    //
    // iOS will not run app code when a notification fires, so there the phrase is rendered to a sound
    // file now and the notification names it; Android speaks at delivery, so there the render answers
    // null and only the payload travels. Rendering is asynchronous, which makes a spoken reminder the
    // one kind that is not on the platform's books the moment this method returns — hence the
    // generation counter: a cancel that lands while the render is in flight must win, or a task the
    // user just completed would still announce itself.

    private readonly ConcurrentDictionary<Guid, int> _spokenGenerations = new();

    private static string SpokenSoundFile(string kind, Guid owner) => $"spoken-{kind}-{owner:N}.caf";

    private void ShowSpoken(Guid owner, string kind, string taskText, IReadOnlyList<NotificationRequest> requests)
    {
        var generation = _spokenGenerations.GetOrAdd(owner, 0);
        var phrase = ReminderPhraseBuilder.Build(taskText);
        var payload = SpokenReminderPayload.Encode(phrase);
        var soundFile = SpokenSoundFile(kind, owner);

        _ = Task.Run(async () =>
        {
            try
            {
                var sound = await _speech.RenderToNotificationSoundAsync(phrase, soundFile).ConfigureAwait(false);

                if (_spokenGenerations.GetOrAdd(owner, 0) != generation)
                {
                    if (sound != null) _speech.DeleteNotificationSound(sound);
                    return;
                }

                foreach (var request in requests)
                {
                    request.ReturningData = payload;
                    if (sound != null) request.Sound = sound;
                    await LocalNotificationCenter.Current.Show(request).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Spoken reminder not scheduled: {ex.Message}");
            }
        });
    }

    private void ForgetSpoken(Guid owner, string kind)
    {
        // Only an owner that was ever spoken has a render to beat; the silent majority leaves no entry.
        while (_spokenGenerations.TryGetValue(owner, out var generation)
               && !_spokenGenerations.TryUpdate(owner, generation + 1, generation))
        {
        }
        _speech.DeleteNotificationSound(SpokenSoundFile(kind, owner));
    }
#endif

    // Fixed, unusual id so the single daily reminder can be reliably updated/cancelled.
    private const int DailyReminderId = 990424;

    public void ScheduleDailyJournalReminder(TimeSpan timeOfDay)
    {
#if ANDROID || IOS || MACCATALYST
        LocalNotificationCenter.Current.Cancel(DailyReminderId);

        var now = DateTime.Now;
        var next = now.Date.Add(timeOfDay);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        var request = new NotificationRequest
        {
            NotificationId = DailyReminderId,
            Title = Diarion.Resources.Localization.AppResources.DailyReminderTitle,
            Description = Diarion.Resources.Localization.AppResources.DailyReminderMessage,
            Schedule = At(next, NotificationRepeat.Daily)
        };

        LocalNotificationCenter.Current.Show(request);
#endif
    }

    public void CancelDailyJournalReminder()
    {
#if ANDROID || IOS || MACCATALYST
        LocalNotificationCenter.Current.Cancel(DailyReminderId);
#endif
    }

    // Reminder ids for a habit: slot 0 = daily; slots 1..7 = weekly per weekday ((int)DayOfWeek + 1).
    private static int HabitReminderId(Guid habitId, int slot) => unchecked(habitId.GetHashCode() * 31 + slot);

    public void ScheduleHabitReminder(Guid habitId, string habitName, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays)
    {
#if ANDROID || IOS || MACCATALYST
        CancelHabitReminder(habitId);

        var title = string.IsNullOrWhiteSpace(habitName)
            ? Diarion.Resources.Localization.AppResources.HabitReminderMessage
            : habitName;
        var description = Diarion.Resources.Localization.AppResources.HabitReminderMessage;
        var now = DateTime.Now;

        if (weekdays == null || weekdays.Count == 0)
        {
            var next = now.Date.Add(timeOfDay);
            if (next <= now) next = next.AddDays(1);

            LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = HabitReminderId(habitId, 0),
                Title = title,
                Description = description,
                Schedule = At(next, NotificationRepeat.Daily)
            });
            return;
        }

        foreach (var day in weekdays.Distinct())
        {
            var next = now.Date.Add(timeOfDay);
            int daysUntil = ((day - (int)next.DayOfWeek) + 7) % 7;
            next = next.AddDays(daysUntil);
            if (next <= now) next = next.AddDays(7);

            LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = HabitReminderId(habitId, day + 1),
                Title = title,
                Description = description,
                Schedule = At(next, NotificationRepeat.Weekly)
            });
        }
#endif
    }

    public void CancelHabitReminder(Guid habitId)
    {
#if ANDROID || IOS || MACCATALYST
        for (int slot = 0; slot <= 7; slot++)
        {
            LocalNotificationCenter.Current.Cancel(HabitReminderId(habitId, slot));
        }
#endif
    }

    // Reminder ids for a task rule: slot 0 = daily, slots 1..7 = weekly per weekday, 8.. = one-shot
    // occurrences. Kept apart from the habit slots by the salt, so two features cannot cancel each other.
    private const int TaskReminderSlots = 40;
    private static int TaskRuleReminderId(Guid ruleId, int slot)
        => unchecked(ruleId.GetHashCode() * 131 + 7919 + slot);

    public void ScheduleRepeatingTaskReminder(Guid ruleId, string title, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays, bool spoken = false)
    {
#if ANDROID || IOS || MACCATALYST
        CancelRepeatingTaskReminder(ruleId);
        var now = DateTime.Now;
        var requests = new List<NotificationRequest>();

        if (weekdays == null || weekdays.Count == 0)
        {
            var next = now.Date.Add(timeOfDay);
            if (next <= now) next = next.AddDays(1);

            requests.Add(new NotificationRequest
            {
                NotificationId = TaskRuleReminderId(ruleId, 0),
                Title = title,
                Schedule = At(next, NotificationRepeat.Daily)
            });
        }
        else
        {
            foreach (var day in weekdays.Distinct())
            {
                var next = now.Date.Add(timeOfDay);
                int daysUntil = ((day - (int)next.DayOfWeek) + 7) % 7;
                next = next.AddDays(daysUntil);
                if (next <= now) next = next.AddDays(7);

                requests.Add(new NotificationRequest
                {
                    NotificationId = TaskRuleReminderId(ruleId, day + 1),
                    Title = title,
                    Schedule = At(next, NotificationRepeat.Weekly)
                });
            }
        }

        ShowAll(ruleId, title, requests, spoken);
#endif
    }

    public void ScheduleTaskOccurrenceReminders(Guid ruleId, string title, IReadOnlyList<DateTime> moments, bool spoken = false)
    {
#if ANDROID || IOS || MACCATALYST
        CancelRepeatingTaskReminder(ruleId);

        var slot = 8;
        var requests = moments
            .Where(m => m > DateTime.Now)
            .Take(TaskReminderSlots - 8)
            .Select(moment => new NotificationRequest
            {
                NotificationId = TaskRuleReminderId(ruleId, slot++),
                Title = title,
                Schedule = At(moment)
            })
            .ToList();

        ShowAll(ruleId, title, requests, spoken);
#endif
    }

#if ANDROID || IOS || MACCATALYST
    private void ShowAll(Guid ruleId, string title, List<NotificationRequest> requests, bool spoken)
    {
        if (requests.Count == 0) return;
        if (spoken)
        {
            ShowSpoken(ruleId, "rule", title, requests);
            return;
        }

        foreach (var request in requests) LocalNotificationCenter.Current.Show(request);
    }
#endif

    public void CancelRepeatingTaskReminder(Guid ruleId)
    {
#if ANDROID || IOS || MACCATALYST
        for (int slot = 0; slot < TaskReminderSlots; slot++)
        {
            LocalNotificationCenter.Current.Cancel(TaskRuleReminderId(ruleId, slot));
        }
        ForgetSpoken(ruleId, "rule");
#endif
    }

    public async Task<bool> RequestPermissionsAsync()
    {
#if ANDROID || IOS || MACCATALYST
        if (await LocalNotificationCenter.Current.AreNotificationsEnabled() == false)
        {
            return await LocalNotificationCenter.Current.RequestNotificationPermission();
        }
#endif
        return await Task.FromResult(true);
    }
}
