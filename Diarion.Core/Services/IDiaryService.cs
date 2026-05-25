using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IDiaryService
{
    Task<List<DiaryEntry>> GetAllEntriesAsync();
    Task<List<DiaryEntry>> GetEntriesForDateAsync(DateTime date);
    Task<DiaryEntry> GetEntryByIdAsync(Guid id);
    Task SaveEntryAsync(DiaryEntry entry);
    Task DeleteEntryAsync(Guid id);

    // Todo functionality
    Task<TodoItem?> GetTodoByIdAsync(Guid id);
    Task<List<TodoItem>> GetTodosForDateAsync(DateTime date);
    Task<List<TodoItem>> GetAllTodosAsync();
    Task SaveTodoAsync(TodoItem todo);
    Task DeleteTodoAsync(Guid todoId);
}
