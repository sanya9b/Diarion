using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Diagnostics;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class TodoService : ITodoService
{
    private readonly IDatabaseContext _dbContext;
    private readonly INotificationService? _notificationService;

    public TodoService(IDatabaseContext dbContext, INotificationService? notificationService = null)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    private ILiteCollection<TodoItem> TodosCollection => _dbContext.GetCollection<TodoItem>("todos");
    private ILiteCollection<UserProfile> ProfileCollection => _dbContext.GetCollection<UserProfile>("profile");

    public Task<IEnumerable<TodoStatsDto>> GetTodosForStatsAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() =>
        {
            var items = TodosCollection.Query()
                .Where(x => x.TargetDate >= startDate && x.TargetDate <= endDate)
                .Select(x => new TodoStatsDto
                {
                    TargetDate = x.TargetDate,
                    IsCompleted = x.IsCompleted
                })
                .ToList();
            return (IEnumerable<TodoStatsDto>)items;
        });
    }

    public Task<TodoStatistics> GetTodoStatsSummaryAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() =>
        {
            // Optimized LiteDB queries that count directly without loading objects into memory
            int total = TodosCollection.Count(x => x.TargetDate >= startDate && x.TargetDate <= endDate);
            int completed = TodosCollection.Count(x => x.TargetDate >= startDate && x.TargetDate <= endDate && x.IsCompleted);
            
            return new TodoStatistics
            {
                TotalCount = total,
                CompletedCount = completed
            };
        });
    }

    public Task<TodoItem?> GetTodoByIdAsync(Guid id)
    {
        return Task.Run<TodoItem?>(() =>
        {
            return TodosCollection.FindById(id);
        });
    }

    public Task<List<TodoItem>> GetTodosForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var startedAt = Stopwatch.GetTimestamp();
            var dateOnly = date.Date;
            var items = TodosCollection.Query()
                .Where(x => x.TargetDate == dateOnly)
                .ToList();

            var profile = ProfileCollection.FindAll().FirstOrDefault();
            bool autoMigrate = profile?.AutoMigrateUncompletedTasksEnabled ?? true;

            if (autoMigrate && dateOnly == DateTime.Today)
            {
                var pastUncompletedTasks = TodosCollection.Query()
                    .Where(x => x.TargetDate < dateOnly && !x.IsCompleted && !x.IsDailyRepeat)
                    .ToList();

                foreach (var task in pastUncompletedTasks)
                {
                    if (task.Priority == TodoPriority.High)
                    {
                        int currentHighCount = items.Count(t => t.Priority == TodoPriority.High && !t.IsCompleted);
                        if (currentHighCount >= 3)
                        {
                            task.Priority = TodoPriority.Medium;
                        }
                    }

                    task.TargetDate = dateOnly;
                    TodosCollection.Update(task);
                    items.Add(task);
                }
            }

            var repeatingPastTasks = TodosCollection.Query()
                .Where(x => x.IsDailyRepeat && x.TargetDate < dateOnly)
                .ToList() 
                .Where(x => x.RepeatEndDate == null || x.RepeatEndDate.Value.Date >= dateOnly)
                .GroupBy(x => string.IsNullOrEmpty(x.RepeatGroupId) ? x.TaskDescription : x.RepeatGroupId)
                .Select(g => g.OrderByDescending(x => x.TargetDate).First())
                .ToList();

            foreach (var task in repeatingPastTasks)
            {
                var alreadyExists = items.Any(x => 
                    (x.RepeatGroupId == task.RepeatGroupId && !string.IsNullOrEmpty(task.RepeatGroupId)) ||
                    (x.TaskDescription == task.TaskDescription && x.IsDailyRepeat));

                if (!alreadyExists)
                {
                    var clonePriority = task.Priority;
                    if (clonePriority == TodoPriority.High)
                    {
                        int currentHighCount = items.Count(t => t.Priority == TodoPriority.High && !t.IsCompleted);
                        if (currentHighCount >= 3)
                        {
                            clonePriority = TodoPriority.Medium;
                        }
                    }

                    var clone = new TodoItem
                    {
                        Id = Guid.NewGuid(),
                        TargetDate = dateOnly,
                        TargetTime = task.TargetTime,
                        HasTime = task.HasTime,
                        TaskDescription = task.TaskDescription,
                        IsCompleted = false, 
                        Priority = clonePriority,
                        CreatedAt = DateTime.Now,
                        IsDailyRepeat = true,
                        RepeatGroupId = task.RepeatGroupId,
                        HasReminder = task.HasReminder
                    };
                    TodosCollection.Insert(clone);
                    items.Add(clone);
                }
            }

            var todos = items
                .OrderBy(x => x.IsCompleted)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.HasTime ? 0 : 1)
                .ThenBy(x => x.TargetTime)
                .ToList();

            StartupTrace.Mark($"TodoService.GetTodosForDateAsync count={todos.Count} duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
            return todos;
        });
    }

    public Task<List<TodoItem>> GetTodosForMonthAsync(int year, int month)
    {
        return Task.Run(() =>
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);
            
            return TodosCollection.Query()
                .Where(x => x.TargetDate >= startDate && x.TargetDate < endDate)
                .ToList();
        });
    }

    public Task<List<TodoItem>> GetAllTodosAsync()
    {
        return Task.Run(() => TodosCollection.FindAll().ToList());
    }

    public Task SaveTodoAsync(TodoItem todo)
    {
        return Task.Run(() =>
        {
            var existing = TodosCollection.FindById(todo.Id);
            
            if (existing != null && existing.IsDailyRepeat && !todo.IsDailyRepeat)
            {
                var groupId = todo.RepeatGroupId ?? todo.TaskDescription;
                var pastRepeats = TodosCollection.Query()
                    .Where(x => x.IsDailyRepeat)
                    .ToList()
                    .Where(x => x.RepeatGroupId == groupId || (string.IsNullOrEmpty(x.RepeatGroupId) && x.TaskDescription == groupId))
                    .ToList();
                    
                foreach (var p in pastRepeats)
                {
                    p.RepeatEndDate = todo.TargetDate.Date;
                    TodosCollection.Update(p);
                }
                
                todo.RepeatEndDate = todo.TargetDate.Date;
            }
            
            if (todo.IsDailyRepeat && string.IsNullOrEmpty(todo.RepeatGroupId))
            {
                todo.RepeatGroupId = Guid.NewGuid().ToString();
            }

            TodosCollection.Upsert(todo);
            UpdateLocalNotification(todo);
        });
    }

    public Task DeleteTodoAsync(Guid todoId)
    {
        return Task.Run(() =>
        {
            TodosCollection.Delete(todoId);
            _notificationService?.CancelTodoReminder(todoId);
        });
    }

    private void UpdateLocalNotification(TodoItem todo)
    {
        if (_notificationService == null) return;
        
        _notificationService.CancelTodoReminder(todo.Id);

        if (!todo.HasReminder || todo.IsCompleted)
            return;

        var targetDateTime = todo.TargetDate.Date;
        if (todo.HasTime)
        {
            targetDateTime = targetDateTime.Add(todo.TargetTime);
        }
        else
        {
            targetDateTime = targetDateTime.AddHours(9); 
        }

        if (targetDateTime > DateTime.Now)
        {
            _notificationService.ScheduleTodoReminder(todo.Id, "Diarion", todo.TaskDescription, targetDateTime);
        }
    }
}