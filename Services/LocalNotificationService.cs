using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core;
using Plugin.LocalNotification.Core.Models;

namespace Diarion.Services;

public class LocalNotificationService : INotificationService
{
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
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = targetTime
                }
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
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = next,
                RepeatType = NotificationRepeat.Daily
            }
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
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = next,
                    RepeatType = NotificationRepeat.Daily
                }
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
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = next,
                    RepeatType = NotificationRepeat.Weekly
                }
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
