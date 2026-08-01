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
    private readonly IProfileService _profileService;

    public DiaryService(IDatabaseContext dbContext, ITodoService todoService, IProfileService profileService)
    {
        _dbContext = dbContext;
        _todoService = todoService;
        _profileService = profileService;
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

    public async Task<StreakResult> GetCurrentStreakAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        var grace = profile?.GetEffectiveStreakGrace() ?? 0;

        return await Task.Run(() =>
        {
            // Note: LiteDB's Query().Select() can sometimes fail to map simple properties correctly in all scenarios.
            // Using FindAll() to project in memory. For a user's local diary, the number of entries is small enough.
            // Blank rows are skipped: browsing a day persists one, so counting rows would count days the
            // user never wrote anything on.
            var dates = EntriesCollection.FindAll()
                .Where(e => e.HasContent())
                .Select(e => e.Date.Date);

            return StreakWalker.Walk(dates, DateTime.Today, grace);
        });
    }

    public Task<IEnumerable<DiaryEntryStatsDto>> GetDiaryEntriesForStatsAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() =>
        {
            var dateOnlyStart = startDate.Date;
            var dateOnlyEnd = endDate.Date;
            
            var items = EntriesCollection.Find(x => x.Date >= dateOnlyStart && x.Date <= dateOnlyEnd).ToList();
            
            var result = items.Select(x => new DiaryEntryStatsDto
            {
                Date = x.Date,
                SleepStart = x.SleepStart,
                SleepEnd = x.SleepEnd,
                SleepQuality = x.SleepQuality,
                Emotion = x.Emotion,
                HourlyMood = x.HourlyMood
            }).ToList();
            
            return (IEnumerable<DiaryEntryStatsDto>)result;
        });
    }

    public Task<IReadOnlyList<PromptAnswerDto>> GetPromptAnswersAsync()
    {
        return Task.Run(() =>
        {
            // Filtered server-side on the answer, so days where the app offered a question and the user
            // never wrote anything never leave the database.
            var items = EntriesCollection
                .Find(x => x.PromptAnswer != null && x.PromptAnswer != string.Empty)
                .Select(x => new PromptAnswerDto
                {
                    EntryId = x.Id,
                    Date = x.Date,
                    PromptReference = x.PromptResourceKey ?? string.Empty,
                    Answer = x.PromptAnswer ?? string.Empty
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Answer))
                .OrderByDescending(x => x.Date)
                .ToList();

            return (IReadOnlyList<PromptAnswerDto>)items;
        });
    }
}