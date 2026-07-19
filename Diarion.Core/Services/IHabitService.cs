using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IHabitService
{
    Task<List<HabitDefinition>> GetActiveHabitsForDateAsync(DateTime date);
    Task AddHabitDefinitionAsync(HabitDefinition habit);
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
