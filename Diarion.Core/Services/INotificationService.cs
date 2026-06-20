using System;
using System.Threading.Tasks;

namespace Diarion.Services;

public interface INotificationService
{
    void ScheduleTodoReminder(Guid todoId, string title, string description, DateTime targetTime);
    void CancelTodoReminder(Guid todoId);
    Task<bool> RequestPermissionsAsync();
}