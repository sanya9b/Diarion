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

    private ILiteCollection<TodoItem> TodosCollection => _dbContext.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
    private ILiteCollection<UserProfile> ProfileCollection => _dbContext.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);
    private ILiteCollection<RecurringTask> RecurringTasksCollection => _dbContext.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection);

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
            // Single ranged scan: project only the completion flag, then count both in memory
            // (avoids traversing the date range twice with two separate Count queries).
            var flags = TodosCollection.Query()
                .Where(x => x.TargetDate >= startDate && x.TargetDate <= endDate)
                .Select(x => x.IsCompleted)
                .ToList();

            return new TodoStatistics
            {
                TotalCount = flags.Count,
                CompletedCount = flags.Count(c => c)
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
                AutoMigratePastTasks(dateOnly, items);
            }

            GenerateRepeatingTasks(dateOnly, items);

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

    private void AutoMigratePastTasks(DateTime dateOnly, List<TodoItem> items)
    {
        var pastUncompletedTasks = TodosCollection.Query()
            .Where(x => x.TargetDate < dateOnly && !x.IsCompleted)
            .ToList()
            // An occurrence of a series belongs to its own day and is never dragged forward — whether the
            // series is still running or was ended long ago. Provenance answers both cases, where the old
            // scheme needed two conditions and a paragraph explaining the second.
            // Filtered in memory because RecurringTaskId is a nullable Guid, which LiteDB's LINQ
            // translation gets wrong by returning nothing — a failure that looks like an ordinary quiet day.
            .Where(x => x.RecurringTaskId == null)
            .ToList();

        foreach (var task in pastUncompletedTasks)
        {
            if (task.Priority == TodoPriority.High)
            {
                int currentHighCount = items.Count(t => t.Priority == TodoPriority.High && !t.IsCompleted);
                if (currentHighCount >= RecurringTaskPlanner.MaxHighPriorityPerDay)
                {
                    task.Priority = TodoPriority.Medium;
                }
            }

            task.TargetDate = dateOnly;
            TodosCollection.Update(task);
            items.Add(task);
        }
    }

    private bool _remindersResynced;

    private void GenerateRepeatingTasks(DateTime dateOnly, List<TodoItem> items)
    {
        var rules = RecurringTasksCollection.FindAll().ToList();
        if (rules.Count == 0) return;

        // Once per session, on the first look at today: one-shot reminders are laid down 90 days out, so
        // without this they would quietly run out for anyone who stops editing their rules.
        if (!_remindersResynced && dateOnly == DateTime.Today)
        {
            _remindersResynced = true;
            foreach (var rule in rules) SyncRuleReminder(rule);
        }

        foreach (var occurrence in RecurringTaskPlanner.PlanForDay(rules, items, dateOnly))
        {
            TodosCollection.Insert(occurrence);
            // Through the notification path rather than around it. The old generator inserted straight
            // into the collection, so a repeating task with a reminder set only ever notified on the
            // instances the user had saved through the form by hand.
            UpdateLocalNotification(occurrence);
            items.Add(occurrence);
        }
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

    public Task<List<TodoItem>> GetTodosForDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() =>
        {
            return TodosCollection.Query()
                .Where(x => x.TargetDate >= startDate && x.TargetDate <= endDate)
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

            if (todo.RecurringTaskId != null && existing != null)
            {
                PropagateTemplateChanges(existing, todo);
            }

            TodosCollection.Upsert(todo);
            UpdateLocalNotification(todo);
        });
    }

    public Task DeleteTodoAsync(Guid todoId)
    {
        return Task.Run(() =>
        {
            var todo = TodosCollection.FindById(todoId);

            // Deleting one occurrence cannot mean "delete the task": the rule would simply produce it
            // again the next time this day was opened. It means "not this day", and the rule is where
            // that has to be remembered.
            if (todo?.RecurringTaskId != null)
            {
                var rule = RecurringTasksCollection.FindById(todo.RecurringTaskId.Value);
                if (rule != null)
                {
                    rule.Skip(todo.TargetDate);
                    RecurringTasksCollection.Update(rule);
                    // A skipped day changes what the reminders should be, and a native repeat cannot miss
                    // a day out — so this may also be what moves the rule onto one-shot scheduling.
                    SyncRuleReminder(rule);
                }
            }

            TodosCollection.Delete(todoId);
            _notificationService?.CancelTodoReminder(todoId);
        });
    }

    public Task DeleteRecurringTaskAsync(Guid ruleId)
    {
        return Task.Run(() =>
        {
            _notificationService?.CancelRepeatingTaskReminder(ruleId);

            // In memory: nullable Guid comparisons are the ones LiteDB mistranslates, and here a wrong
            // empty result would look like a series that had already been cleaned up.
            var outstanding = TodosCollection.FindAll()
                .Where(t => t.RecurringTaskId == ruleId && !t.IsCompleted)
                .ToList();

            foreach (var todo in outstanding)
            {
                TodosCollection.Delete(todo.Id);
                _notificationService?.CancelTodoReminder(todo.Id);
            }

            // Completed rows keep pointing at a rule that no longer exists, exactly as posted finance rows
            // do. Nulling it would erase where they came from, and auto-migration already leaves them be.
            RecurringTasksCollection.Delete(ruleId);
        });
    }

    /// <summary>
    /// Clears the days a series had already been materialized onto beyond the given one. Ending a series
    /// only stops the rule from producing new days; the rows for days the user had already scrolled to are
    /// on disk, and without this the task goes on standing in next week after the repeat was switched off.
    /// Completed rows stay, as they do when the whole series is deleted — they are what happened.
    /// </summary>
    private void DeleteOccurrencesAfter(Guid ruleId, DateTime lastKeptDate)
    {
        var cutoff = lastKeptDate.Date;

        // In memory: nullable Guid comparisons are the ones LiteDB mistranslates, and a wrong empty
        // result here would look exactly like a series that had nothing left to clean up.
        var future = TodosCollection.FindAll()
            .Where(t => t.RecurringTaskId == ruleId && !t.IsCompleted && t.TargetDate > cutoff)
            .ToList();

        foreach (var todo in future)
        {
            TodosCollection.Delete(todo.Id);
            _notificationService?.CancelTodoReminder(todo.Id);
        }
    }

    public Task<RecurringTask?> GetRecurringTaskAsync(Guid ruleId)
        => Task.Run<RecurringTask?>(() => RecurringTasksCollection.FindById(ruleId));

    public Task<List<RecurringTask>> GetRecurringTasksAsync()
        => Task.Run(() => RecurringTasksCollection.FindAll().ToList());

    public Task SetRecurrenceAsync(Guid todoId, RecurrenceRule? recurrence)
    {
        return Task.Run(() =>
        {
            var todo = TodosCollection.FindById(todoId);
            if (todo == null) return;

            var rule = todo.RecurringTaskId == null
                ? null
                : RecurringTasksCollection.FindById(todo.RecurringTaskId.Value);

            if (recurrence == null)
            {
                if (rule == null) return;

                // End the series the day BEFORE this instance. This row already exists on its own day, so
                // ending today would have the rule claim an occurrence that is already there. Its own
                // provenance keeps it pinned against auto-migration, and keeps it deleted if deleted.
                rule.Recurrence.EndDate = todo.TargetDate.AddDays(-1);

                DeleteOccurrencesAfter(rule.Id, todo.TargetDate);

                // Ended before it ever began: the rule cannot produce a single day now, so keeping it
                // would only leave a row for every day load to read past. Rows already written keep
                // pointing at it, exactly as they do after the whole series is deleted.
                if (rule.Recurrence.EndDate < rule.Recurrence.Anchor)
                {
                    RecurringTasksCollection.Delete(rule.Id);
                    // Not SyncRuleReminder: cancelling clears every slot the rule owns, native repeat and
                    // one-shots alike, and there is no longer a rule to schedule anything from.
                    _notificationService?.CancelRepeatingTaskReminder(rule.Id);
                    return;
                }

                RecurringTasksCollection.Update(rule);
                SyncRuleReminder(rule);
                return;
            }

            // An ended series is history. Switching Repeat back on for a row it left behind means "start
            // repeating again from here", not "undo the ending" — reusing the old rule would carry its
            // original anchor and backfill every day in between the next time they were opened.
            if (rule != null && !rule.IsActiveOn(todo.TargetDate)) rule = null;

            if (rule == null)
            {
                rule = new RecurringTask
                {
                    TaskDescription = todo.TaskDescription,
                    Priority = todo.Priority,
                    HasTime = todo.HasTime,
                    TargetTime = todo.TargetTime,
                    EndTime = todo.EndTime,
                    HasReminder = todo.HasReminder,
                    Recurrence = recurrence
                };
                if (rule.Recurrence.Anchor == default) rule.Recurrence.Anchor = todo.TargetDate;

                RecurringTasksCollection.Insert(rule);
                todo.RecurringTaskId = rule.Id;
                TodosCollection.Update(todo);
                SyncRuleReminder(rule);
                return;
            }

            // Editing which days a series lands on must not move where it started, or every occurrence
            // before today would vanish from the days the user already planned around.
            recurrence.Anchor = rule.Recurrence.Anchor;
            rule.Recurrence = recurrence;
            RecurringTasksCollection.Update(rule);
            SyncRuleReminder(rule);
        });
    }

    /// <summary>
    /// How far ahead one-shot reminders are laid down for the rules no platform repeat can express.
    /// Re-laid whenever the rule is touched and once per session on the first read of today, so the
    /// window only runs out for someone who has not opened the app in three months.
    /// </summary>
    private const int ReminderLookaheadDays = 90;

    /// <summary>
    /// Puts the rule's reminder where the operating system will fire it without the app being opened.
    /// Materializing an occurrence is what used to schedule the notification, and materializing is lazy,
    /// so a task set for next Friday stayed silent unless the user happened to look at next Friday — which
    /// is precisely the day they were relying on being told about.
    /// </summary>
    private void SyncRuleReminder(RecurringTask rule)
    {
        if (_notificationService == null) return;

        _notificationService.CancelRepeatingTaskReminder(rule.Id);

        var recurrence = rule.Recurrence ?? new RecurrenceRule();
        if (!rule.HasReminder || !rule.HasTime) return;
        if (recurrence.EndDate != null && recurrence.EndDate < DateTime.Today) return;

        // The platform's own repeat only says "every day" or "every weekday N" — it cannot stop on a date
        // and cannot miss one out. Anything bounded or skipped is laid down occurrence by occurrence.
        var canRepeatNatively =
            recurrence.EndDate == null
            && rule.SkippedDates.Count == 0
            && recurrence.Kind is RecurrenceKind.Daily or RecurrenceKind.Weekly;

        if (canRepeatNatively)
        {
            _notificationService.ScheduleRepeatingTaskReminder(
                rule.Id,
                rule.TaskDescription,
                rule.TargetTime,
                recurrence.Kind == RecurrenceKind.Weekly ? recurrence.DaysOfWeek : null);
            return;
        }

        var moments = recurrence
            .Enumerate(DateTime.Today, DateTime.Today.AddDays(ReminderLookaheadDays))
            .Where(day => !rule.IsSkipped(day))
            .Select(day => day.Add(rule.TargetTime))
            .Where(moment => moment > DateTime.Now)
            .ToList();

        _notificationService.ScheduleTaskOccurrenceReminders(rule.Id, rule.TaskDescription, moments);
    }

    /// <summary>
    /// Carries a field the user just changed on one occurrence onto the whole series. Compared against the
    /// stored row rather than against the rule on purpose: that is what makes this "changed in this save"
    /// rather than "differs from the template", so ticking an old occurrence complete cannot quietly undo
    /// a rename made after it was created.
    /// </summary>
    private void PropagateTemplateChanges(TodoItem existing, TodoItem todo)
    {
        var rule = RecurringTasksCollection.FindById(todo.RecurringTaskId!.Value);
        if (rule == null) return;

        var changed = false;

        if (existing.TaskDescription != todo.TaskDescription) { rule.TaskDescription = todo.TaskDescription; changed = true; }
        if (existing.Priority != todo.Priority) { rule.Priority = todo.Priority; changed = true; }
        if (existing.HasTime != todo.HasTime) { rule.HasTime = todo.HasTime; changed = true; }
        if (existing.TargetTime != todo.TargetTime) { rule.TargetTime = todo.TargetTime; changed = true; }
        if (existing.EndTime != todo.EndTime) { rule.EndTime = todo.EndTime; changed = true; }
        if (existing.HasReminder != todo.HasReminder) { rule.HasReminder = todo.HasReminder; changed = true; }

        if (!changed) return;
        RecurringTasksCollection.Update(rule);
        SyncRuleReminder(rule);
    }

    public Task DeleteTodosByDiaryEntryAsync(Guid diaryEntryId)
    {
        return Task.Run(() =>
        {
            var todos = TodosCollection.Find(x => x.DiaryEntryId == diaryEntryId).ToList();
            TodosCollection.DeleteMany(x => x.DiaryEntryId == diaryEntryId);
            
            if (_notificationService != null)
            {
                foreach (var todo in todos)
                {
                    _notificationService.CancelTodoReminder(todo.Id);
                }
            }
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