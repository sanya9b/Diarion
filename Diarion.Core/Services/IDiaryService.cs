using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IDiaryService
{
    // Profile
    Task<UserProfile> GetUserProfileAsync();
    Task SaveUserProfileAsync(UserProfile profile);

    // Habits
    Task<List<HabitDefinition>> GetActiveHabitsForDateAsync(DateTime date);
    Task AddHabitDefinitionAsync(HabitDefinition habit);
    Task DeleteHabitDefinitionAsync(Guid id, DateTime deleteDate);

    Task<List<DiaryEntry>> GetAllEntriesAsync();
    Task<DiaryEntry> GetEntryForDateAsync(DateTime date);
    Task<DiaryEntry> GetEntryByIdAsync(Guid id);
    Task SaveEntryAsync(DiaryEntry entry);
    Task DeleteEntryAsync(Guid id);

    // Todo functionality
    Task<TodoItem?> GetTodoByIdAsync(Guid id);
    Task<List<TodoItem>> GetTodosForDateAsync(DateTime date);
    Task<List<TodoItem>> GetTodosForMonthAsync(int year, int month);
    Task<List<TodoItem>> GetAllTodosAsync();
    Task SaveTodoAsync(TodoItem todo);
    Task DeleteTodoAsync(Guid todoId);
}
