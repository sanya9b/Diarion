using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services.Database;
using Diarion.Services.Store;
using LiteDB;

namespace Diarion.Services;

public class DiaryService : IDiaryService
{
    private readonly IDatabaseContext _dbContext;
    private readonly ITodoService _todoService;
    private readonly IProfileService _profileService;
    private readonly IAccessGuard _accessGuard;

    public DiaryService(IDatabaseContext dbContext, ITodoService todoService, IProfileService profileService, IAccessGuard? accessGuard = null)
    {
        _accessGuard = accessGuard ?? AccessGuard.Unrestricted;
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

    public async Task SaveEntryAsync(DiaryEntry entry)
    {
        _accessGuard.EnsureWritable();
        await Task.Run(() => EntriesCollection.Upsert(entry));
        Announce(entry.Id);
    }

    public async Task DeleteEntryAsync(Guid id)
    {
        _accessGuard.EnsureWritable();
        await Task.Run(() => EntriesCollection.Delete(id));
        await _todoService.DeleteTodosByDiaryEntryAsync(id);
        Announce(id);
    }

    private static void Announce(Guid id) =>
        WeakReferenceMessenger.Default.Send(
            new DocumentChangedMessage(DocumentKind.Diary, id.ToString()));

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

    public Task<IReadOnlyList<DateTime>> GetContentDatesAsync(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return Task.Run(() => (IReadOnlyList<DateTime>)EntriesCollection
            .Find(x => x.Date >= start && x.Date <= end)
            .Where(e => e.HasContent())
            .Select(e => e.Date.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList());
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
                HourlyMood = x.HourlyMood,
                HabitCompletion = x.HabitsList is { Count: > 0 }
                    ? (double)x.HabitsList.Count(h => h.IsCompleted) / x.HabitsList.Count
                    : null,
                MealsLogged = CountMeals(x)
            }).ToList();
            
            return (IEnumerable<DiaryEntryStatsDto>)result;
        });
    }

    private static int CountMeals(DiaryEntry entry)
    {
        var count = 0;
        if (entry.IsBreakfastDone) count++;
        if (entry.IsSecondBreakfastDone) count++;
        if (entry.IsLunchDone) count++;
        if (entry.IsSnackDone) count++;
        if (entry.IsDinnerDone) count++;
        return count;
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