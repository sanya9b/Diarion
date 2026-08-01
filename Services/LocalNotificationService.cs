using System;
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

    public void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime)
    {
#if ANDROID || IOS || MACCATALYST
        int notificationId = todoId.GetHashCode();
        LocalNotificationCenter.Current.Cancel(notificationId);

        if (targetTime > DateTime.Now)
        {
            var request = new NotificationRequest
            {
                NotificationId = notificationId,
                Title = title,
                Description = description,
                Schedule = At(targetTime)
            };

            LocalNotificationCenter.Current.Show(request);
        }
#endif
    }

    public void CancelTodoReminder(Guid todoId)
    {
#if ANDROID || IOS || MACCATALYST
        LocalNotificationCenter.Current.Cancel(todoId.GetHashCode());
#endif
    }

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

    public void ScheduleRepeatingTaskReminder(Guid ruleId, string title, TimeSpan timeOfDay, IReadOnlyList<int>? weekdays)
    {
#if ANDROID || IOS || MACCATALYST
        CancelRepeatingTaskReminder(ruleId);
        var now = DateTime.Now;

        if (weekdays == null || weekdays.Count == 0)
        {
            var next = now.Date.Add(timeOfDay);
            if (next <= now) next = next.AddDays(1);

            LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = TaskRuleReminderId(ruleId, 0),
                Title = title,
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
                NotificationId = TaskRuleReminderId(ruleId, day + 1),
                Title = title,
                Schedule = At(next, NotificationRepeat.Weekly)
            });
        }
#endif
    }

    public void ScheduleTaskOccurrenceReminders(Guid ruleId, string title, IReadOnlyList<DateTime> moments)
    {
#if ANDROID || IOS || MACCATALYST
        CancelRepeatingTaskReminder(ruleId);

        var slot = 8;
        foreach (var moment in moments.Where(m => m > DateTime.Now).Take(TaskReminderSlots - 8))
        {
            LocalNotificationCenter.Current.Show(new NotificationRequest
            {
                NotificationId = TaskRuleReminderId(ruleId, slot++),
                Title = title,
                Schedule = At(moment)
            });
        }
#endif
    }

    public void CancelRepeatingTaskReminder(Guid ruleId)
    {
#if ANDROID || IOS || MACCATALYST
        for (int slot = 0; slot < TaskReminderSlots; slot++)
        {
            LocalNotificationCenter.Current.Cancel(TaskRuleReminderId(ruleId, slot));
        }
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
