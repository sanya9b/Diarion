using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IDiaryHabitSyncService
{
    Task SyncHabitsForEntryAsync(DiaryEntry entry);
}

public class DiaryHabitSyncService : IDiaryHabitSyncService
{
    private readonly IHabitService _habitService;

    public DiaryHabitSyncService(IHabitService habitService)
    {
        _habitService = habitService;
    }

    public async Task SyncHabitsForEntryAsync(DiaryEntry entry)
    {
        var activeDefs = await _habitService.GetActiveHabitsForDateAsync(entry.Date);
        var validIds = new HashSet<Guid>(activeDefs.Select(d => d.Id));
        
        var filteredHabits = entry.HabitsList.Where(h => validIds.Contains(h.HabitId)).ToList();
        
        var existingIds = new HashSet<Guid>(filteredHabits.Select(h => h.HabitId));
        foreach (var def in activeDefs)
        {
            if (!existingIds.Contains(def.Id))
            {
                filteredHabits.Add(new HabitItem { HabitId = def.Id, Name = def.Name });
            }
            else
            {
                var h = filteredHabits.First(x => x.HabitId == def.Id);
                if (h.Name != def.Name) h.Name = def.Name;
            }
        }

        entry.HabitsList = filteredHabits.OrderBy(h => 
        {
            var def = activeDefs.FirstOrDefault(d => d.Id == h.HabitId);
            return def?.Order ?? int.MaxValue;
        }).ThenBy(h => 
        {
            var def = activeDefs.FirstOrDefault(d => d.Id == h.HabitId);
            return def?.CreatedAt ?? DateTime.MaxValue;
        }).ToList();
    }
}
