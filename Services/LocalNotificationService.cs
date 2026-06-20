using System;
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
