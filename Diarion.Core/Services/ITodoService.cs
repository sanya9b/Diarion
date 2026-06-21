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
    Task DeleteTodosByDiaryEntryAsync(Guid diaryEntryId);
    Task<IEnumerable<TodoStatsDto>> GetTodosForStatsAsync(DateTime startDate, DateTime endDate);
    Task<TodoStatistics> GetTodoStatsSummaryAsync(DateTime startDate, DateTime endDate);
}
