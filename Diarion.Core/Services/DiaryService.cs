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
    private readonly ITodoService _todoService;

    public DiaryService(IDatabaseContext dbContext, ITodoService todoService)
    {
        _dbContext = dbContext;
        _todoService = todoService;
    }

    private ILiteCollection<DiaryEntry> EntriesCollection => _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

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

    public Task<int> GetCurrentStreakAsync()
    {
        return Task.Run(() =>
        {
            var dates = EntriesCollection.Query()
                .Select(x => x.Date)
                .ToEnumerable()
                .Select(d => d.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            if (!dates.Any()) return 0;

            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);

            int streak = 0;
            DateTime currentDateToCheck;

            if (dates[0] == today)
            {
                currentDateToCheck = today;
            }
            else if (dates[0] == yesterday)
            {
                currentDateToCheck = yesterday;
            }
            else
            {
                // Last entry was before yesterday, streak is broken
                return 0;
            }

            foreach (var date in dates)
            {
                if (date == currentDateToCheck)
                {
                    streak++;
                    currentDateToCheck = currentDateToCheck.AddDays(-1);
                }
                else
                {
                    break; // Streak broken
                }
            }

            return streak;
        });
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