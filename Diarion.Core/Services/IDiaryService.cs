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
    Task<List<HarmfulHabitTracker>> GetHarmfulHabitTrackersAsync();
    Task SaveHarmfulHabitTrackerAsync(HarmfulHabitTracker tracker);
    Task SetHarmfulHabitDayMarkedAsync(Guid trackerId, DateTime date, bool isMarked);
    Task<List<ReadingTrackerBook>> GetReadingTrackerBooksAsync();
    Task SaveReadingTrackerBookAsync(ReadingTrackerBook book);
    Task DeleteReadingTrackerBookAsync(int slotNumber);
    Task<List<HappyMoment>> GetHappyMomentsAsync();
    Task SaveHappyMomentAsync(HappyMoment moment);
    Task<List<GoodDeed>> GetGoodDeedsAsync();
    Task SaveGoodDeedAsync(GoodDeed deed);
    Task DeleteGoodDeedAsync(int slotNumber);

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
