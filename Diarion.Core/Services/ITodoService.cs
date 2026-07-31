using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface ITodoService
{
    Task<TodoItem?> GetTodoByIdAsync(Guid id);
    Task<List<TodoItem>> GetTodosForDateAsync(DateTime date);
    Task<List<TodoItem>> GetTodosForMonthAsync(int year, int month);
    Task<List<TodoItem>> GetTodosForDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<TodoItem>> GetAllTodosAsync();
    Task SaveTodoAsync(TodoItem todo);
    Task DeleteTodoAsync(Guid todoId);

    /// <summary>The rule behind a repeating task, or null if this one does not repeat.</summary>
    Task<RecurringTask?> GetRecurringTaskAsync(Guid ruleId);

    /// <summary>
    /// Starts, changes or ends the series a task belongs to. Null ends it. Separate from
    /// <see cref="SaveTodoAsync"/> rather than an argument on it because a nullable rule cannot say
    /// "leave this alone", which is what every caller that only ticks a task complete needs to say.
    /// </summary>
    Task SetRecurrenceAsync(Guid todoId, RecurrenceRule? recurrence);
    Task DeleteTodosByDiaryEntryAsync(Guid diaryEntryId);
    Task<IEnumerable<TodoStatsDto>> GetTodosForStatsAsync(DateTime startDate, DateTime endDate);
    Task<TodoStatistics> GetTodoStatsSummaryAsync(DateTime startDate, DateTime endDate);
}
