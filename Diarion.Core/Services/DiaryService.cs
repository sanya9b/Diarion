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

public class DiaryService : IDiaryService
{
    private readonly IDatabaseContext _dbContext;
    // We still need IHabitService to sync habits when creating a new DiaryEntry
    private readonly IHabitService _habitService;
    private readonly ITodoService _todoService;

    public DiaryService(IDatabaseContext dbContext, IHabitService habitService, ITodoService todoService)
    {
        _dbContext = dbContext;
        _habitService = habitService;
        _todoService = todoService;
    }

    private ILiteCollection<DiaryEntry> EntriesCollection => _dbContext.GetCollection<DiaryEntry>("entries");

    public Task<List<DiaryEntry>> GetAllEntriesAsync()
    {
        return Task.Run(() => EntriesCollection.Query().OrderByDescending(x => x.Date).ToList());
    }

    public async Task<DiaryEntry> GetEntryForDateAsync(DateTime date)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var dateOnly = date.Date;
        
        var entry = await Task.Run(() => EntriesCollection.Query()
            .Where(x => x.Date == dateOnly)
            .FirstOrDefault());

        if (entry == null)
        {
            entry = new DiaryEntry 
            { 
                Date = dateOnly,
                CreatedAt = DateTime.Now
            };
        }

        // Синхронізація звичок для цього дня
        var activeDefs = await _habitService.GetActiveHabitsForDateAsync(dateOnly);
        var validIds = new HashSet<Guid>(activeDefs.Select(d => d.Id));
        
        // Залишаємо тільки ті звички, які були актуальні на цей день
        var filteredHabits = entry.Habits.Where(h => validIds.Contains(h.HabitId)).ToList();
        
        // Додаємо нові звички, яких ще немає в цьому записі
        var existingIds = new HashSet<Guid>(filteredHabits.Select(h => h.HabitId));
        foreach (var def in activeDefs)
        {
            if (!existingIds.Contains(def.Id))
            {
                filteredHabits.Add(new HabitItem { HabitId = def.Id, Name = def.Name });
            }
            else
            {
                // Оновлюємо ім'я, якщо воно змінилося
                var h = filteredHabits.First(x => x.HabitId == def.Id);
                if (h.Name != def.Name) h.Name = def.Name;
            }
        }

        // Відновлюємо колекцію у правильному порядку
        entry.Habits = new System.Collections.ObjectModel.ObservableCollection<HabitItem>(filteredHabits.OrderBy(h => 
        {
            var def = activeDefs.FirstOrDefault(d => d.Id == h.HabitId);
            return def?.Order ?? int.MaxValue;
        }).ThenBy(h => 
        {
            var def = activeDefs.FirstOrDefault(d => d.Id == h.HabitId);
            return def?.CreatedAt ?? DateTime.MaxValue;
        }));

        StartupTrace.Mark($"DiaryService.GetEntryForDateAsync duration={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms");
        return entry;
    }

    public Task<DiaryEntry> GetEntryByIdAsync(Guid id)
    {
        return Task.Run(() => EntriesCollection.FindById(id));
    }

    public Task SaveEntryAsync(DiaryEntry entry)
    {
        return Task.Run(() => EntriesCollection.Upsert(entry));
    }

    public async Task DeleteEntryAsync(Guid id)
    {
        await Task.Run(() => EntriesCollection.Delete(id));
        await _todoService.DeleteTodosByDiaryEntryAsync(id);
    }

    public Task<IEnumerable<DiaryEntryStatsDto>> GetDiaryEntriesForStatsAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() =>
        {
            var items = EntriesCollection.Query()
                .Where(x => x.Date >= startDate && x.Date <= endDate)
                .Select(x => new DiaryEntryStatsDto
                {
                    Date = x.Date,
                    SleepStart = x.SleepStart,
                    SleepEnd = x.SleepEnd,
                    SleepQuality = x.SleepQuality,
                    Emotion = x.Emotion
                })
                .ToList();
            return (IEnumerable<DiaryEntryStatsDto>)items;
        });
    }
}