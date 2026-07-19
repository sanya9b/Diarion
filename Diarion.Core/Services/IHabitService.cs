using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IHabitService
{
    Task<List<HabitDefinition>> GetActiveHabitsForDateAsync(DateTime date);

    /// <summary>
    /// Completion history for each good habit that was active within [<paramref name="start"/>,
    /// <paramref name="end"/>], read from the diary entries' habit lists. Used for strength/streak/heatmap.
    /// </summary>
    Task<List<HabitCompletionHistory>> GetHabitCompletionsAsync(DateTime start, DateTime end);

    Task AddHabitDefinitionAsync(HabitDefinition habit);
    Task<HabitDefinition?> GetHabitDefinitionByIdAsync(Guid id);
    Task UpdateHabitDefinitionAsync(HabitDefinition habit);
    Task DeleteHabitDefinitionAsync(Guid id, DateTime deleteDate);
    Task UpdateHabitDefinitionsOrderAsync(List<Guid> orderedIds);
    
    Task<List<HarmfulHabitTracker>> GetHarmfulHabitTrackersAsync();
    Task<HarmfulHabitTracker?> GetHarmfulHabitTrackerByIdAsync(Guid id);
    Task SaveHarmfulHabitTrackerAsync(HarmfulHabitTracker tracker);
    Task SetHarmfulHabitDayMarkedAsync(Guid trackerId, DateTime date, bool isMarked);
    Task DeleteHarmfulHabitTrackerAsync(Guid id);

    /// <summary>Logs a relapse for the tracker (clamped to [StartDate, today]); resets the clean streak.</summary>
    Task AddRelapseAsync(Guid trackerId, DateTime date, string? note);
}
