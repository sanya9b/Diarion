using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class HabitServiceTests : IDisposable
{
    private readonly DatabaseContext _db;
    private readonly HabitService _service;

    public HabitServiceTests()
    {
        _db = new DatabaseContext(useInMemory: true);
        _service = new HabitService(_db);
    }

    [Fact]
    public async Task GetHabitCompletionsAsync_FoldsEntryHabitLists()
    {
        var today = DateTime.Today;
        var habitId = Guid.NewGuid();
        await _service.AddHabitDefinitionAsync(new HabitDefinition
        {
            Id = habitId,
            Name = "Water",
            CreatedAt = today.AddDays(-10)
        });

        var entries = _db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        entries.Insert(new DiaryEntry
        {
            Date = today.AddDays(-1),
            HabitsList = { new HabitItem { HabitId = habitId, Name = "Water", IsCompleted = true } }
        });
        entries.Insert(new DiaryEntry
        {
            Date = today,
            HabitsList = { new HabitItem { HabitId = habitId, Name = "Water", IsCompleted = false } }
        });

        var histories = await _service.GetHabitCompletionsAsync(today.AddDays(-7), today);

        var h = histories.Should().ContainSingle(x => x.HabitId == habitId).Subject;
        h.Name.Should().Be("Water");
        h.CompletedDates.Should().Contain(today.AddDays(-1));
        h.CompletedDates.Should().NotContain(today); // logged but not completed
    }

    [Fact]
    public async Task GetHabitCompletionsAsync_ExcludesHabitsCreatedAfterWindow()
    {
        var today = DateTime.Today;
        await _service.AddHabitDefinitionAsync(new HabitDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Future",
            CreatedAt = today.AddDays(1)
        });

        var histories = await _service.GetHabitCompletionsAsync(today.AddDays(-7), today);

        histories.Should().NotContain(x => x.Name == "Future");
    }

    [Fact]
    public async Task GetActiveHabitsForDateAsync_ExcludesHabitsNotScheduledForThatWeekday()
    {
        // Most recent Monday on/before today, and the following day (Tuesday).
        var monday = DateTime.Today;
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
        var tuesday = monday.AddDays(1);

        await _service.AddHabitDefinitionAsync(new HabitDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Gym",
            CreatedAt = monday.AddDays(-7),
            Schedule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new() { (int)DayOfWeek.Monday } }
        });

        var onMonday = await _service.GetActiveHabitsForDateAsync(monday);
        var onTuesday = await _service.GetActiveHabitsForDateAsync(tuesday);

        onMonday.Should().Contain(h => h.Name == "Gym");
        onTuesday.Should().NotContain(h => h.Name == "Gym");
    }

    public void Dispose() => _db.Dispose();
}
