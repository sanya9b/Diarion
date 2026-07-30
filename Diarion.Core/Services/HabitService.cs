using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class HabitService : IHabitService
{
    private readonly IDatabaseContext _dbContext;

    public HabitService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<HabitDefinition> HabitsCollection => _dbContext.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
    private ILiteCollection<HarmfulHabitTracker> HarmfulHabitTrackersCollection => _dbContext.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection);
    private ILiteCollection<DiaryEntry> EntriesCollection => _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

    public Task<List<HabitDefinition>> GetActiveHabitsForDateAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var dateOnly = date.Date;
            var defs = HabitsCollection.Query()
                .Where(x => x.CreatedAt <= dateOnly && (x.DeletedAt == null || x.DeletedAt > dateOnly))
                .ToList()
                // Only surface habits scheduled for this weekday (IsScheduledOn isn't LiteDB-queryable).
                .Where(x => x.IsScheduledOn(dateOnly))
                .ToList();

            // Built-in default habits are re-localized to the current UI language here so they
            // stay bilingual regardless of the language active when the database was seeded.
            foreach (var def in defs)
            {
                var localized = HabitLocalization.ResolveName(def);
                if (!string.IsNullOrEmpty(localized))
                    def.Name = localized;
            }

            return defs;
        });
    }

    public Task<List<HabitCompletionHistory>> GetHabitCompletionsAsync(DateTime start, DateTime end)
    {
        return Task.Run(() =>
        {
            var startDate = start.Date;
            var endDate = end.Date;

            // Habits that existed at any point in the window (created on/before the end, not deleted before the start).
            var defs = HabitsCollection.Query()
                .Where(x => x.CreatedAt <= endDate && (x.DeletedAt == null || x.DeletedAt > startDate))
                .ToList()
                .OrderBy(d => d.Order)
                .ThenBy(d => d.CreatedAt)
                .ToList();

            var byId = new Dictionary<Guid, HabitCompletionHistory>();
            var result = new List<HabitCompletionHistory>();
            foreach (var d in defs)
            {
                var name = HabitLocalization.ResolveName(d);
                if (string.IsNullOrEmpty(name)) name = d.Name;

                var hist = new HabitCompletionHistory
                {
                    HabitId = d.Id,
                    Name = name,
                    CreatedAt = d.CreatedAt.Date,
                    Schedule = d.Schedule ?? new RecurrenceRule(),
                    Target = d.Target,
                    CompletedDates = new HashSet<DateTime>()
                };
                byId[d.Id] = hist;
                result.Add(hist);
            }

            var entries = EntriesCollection.Find(x => x.Date >= startDate && x.Date <= endDate).ToList();
            foreach (var entry in entries)
            {
                var day = entry.Date.Date;
                foreach (var h in entry.HabitsList)
                {
                    if (h.IsCompleted && byId.TryGetValue(h.HabitId, out var hist))
                    {
                        hist.CompletedDates.Add(day);
                    }
                }
            }

            return result;
        });
    }

    public Task AddHabitDefinitionAsync(HabitDefinition habit)
    {
        return Task.Run(() => HabitsCollection.Insert(habit));
    }

    public Task<HabitDefinition?> GetHabitDefinitionByIdAsync(Guid id)
    {
        return Task.Run(() => (HabitDefinition?)HabitsCollection.FindById(id));
    }

    public Task UpdateHabitDefinitionAsync(HabitDefinition habit)
    {
        return Task.Run(() => HabitsCollection.Update(habit));
    }

    public Task DeleteHabitDefinitionAsync(Guid id, DateTime deleteDate)
    {
        return Task.Run(() =>
        {
            var def = HabitsCollection.FindById(id);
            if (def != null)
            {
                def.DeletedAt = deleteDate.Date;
                HabitsCollection.Update(def);
            }
        });
    }

    public Task UpdateHabitDefinitionsOrderAsync(List<Guid> orderedIds)
    {
        return Task.Run(() =>
        {
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var def = HabitsCollection.FindById(orderedIds[i]);
                if (def != null)
                {
                    def.Order = i;
                    HabitsCollection.Update(def);
                }
            }
        });
    }

    public Task<List<HarmfulHabitTracker>> GetHarmfulHabitTrackersAsync()
    {
        return Task.Run(() => HarmfulHabitTrackersCollection.Query().OrderByDescending(x => x.CreatedAt).ToList());
    }

    public Task<HarmfulHabitTracker?> GetHarmfulHabitTrackerByIdAsync(Guid id)
    {
        return Task.Run(() => (HarmfulHabitTracker?)HarmfulHabitTrackersCollection.FindById(id));
    }

    public Task SaveHarmfulHabitTrackerAsync(HarmfulHabitTracker tracker)
    {
        return Task.Run(() =>
        {
            var normalizedName = (tracker.HarmfulHabitName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException("Tracker name is required.", nameof(tracker));
            }

            var normalizedStartDate = tracker.StartDate.Date > DateTime.Today ? DateTime.Today : tracker.StartDate.Date;
            var hasDuplicate = HarmfulHabitTrackersCollection.FindAll()
                .Any(x => x.Id != tracker.Id && string.Equals(x.HarmfulHabitName.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (hasDuplicate)
            {
                throw new InvalidOperationException("Tracker with the same name already exists.");
            }

            tracker.HarmfulHabitName = normalizedName;
            tracker.StartDate = normalizedStartDate;
            tracker.CreatedAt = tracker.CreatedAt == default ? DateTime.UtcNow : tracker.CreatedAt;
            tracker.MarkedDays = (tracker.MarkedDays ?? new List<DateTime>())
                .Select(x => x.Date)
                .Where(x => x >= tracker.StartDate && x <= DateTime.Today)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            HarmfulHabitTrackersCollection.Upsert(tracker);
        });
    }

    public Task DeleteHarmfulHabitTrackerAsync(Guid id)
    {
        return Task.Run(() =>
        {
            HarmfulHabitTrackersCollection.Delete(id);
        });
    }

    public Task AddRelapseAsync(Guid trackerId, DateTime date, string? note)
    {
        return Task.Run(() =>
        {
            var tracker = HarmfulHabitTrackersCollection.FindById(trackerId)
                ?? throw new InvalidOperationException("Tracker was not found.");

            var d = date.Date;
            if (d < tracker.StartDate.Date) d = tracker.StartDate.Date;
            if (d > DateTime.Today) d = DateTime.Today;

            tracker.Relapses ??= new List<RelapseEvent>();
            tracker.Relapses.Add(new RelapseEvent { Date = d, Note = (note ?? string.Empty).Trim() });
            tracker.Relapses = tracker.Relapses.OrderBy(r => r.Date).ToList();

            HarmfulHabitTrackersCollection.Update(tracker);
        });
    }

    public Task SetHarmfulHabitDayMarkedAsync(Guid trackerId, DateTime date, bool isMarked)
    {
        return Task.Run(() =>
        {
            var tracker = HarmfulHabitTrackersCollection.FindById(trackerId)
                ?? throw new InvalidOperationException("Tracker was not found.");

            var targetDate = date.Date;
            if (targetDate < tracker.StartDate.Date || targetDate > DateTime.Today)
            {
                return;
            }

            tracker.MarkedDays ??= new List<DateTime>();
            tracker.MarkedDays = tracker.MarkedDays
                .Select(x => x.Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (isMarked)
            {
                if (!tracker.MarkedDays.Contains(targetDate))
                {
                    tracker.MarkedDays.Add(targetDate);
                }
            }
            else
            {
                tracker.MarkedDays.RemoveAll(x => x == targetDate);
            }

            tracker.MarkedDays = tracker.MarkedDays.OrderBy(x => x).ToList();
            HarmfulHabitTrackersCollection.Update(tracker);
        });
    }
}