using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class QuitTrackerServiceTests : IDisposable
{
    private readonly DatabaseContext _db;
    private readonly HabitService _service;

    public QuitTrackerServiceTests()
    {
        _db = new DatabaseContext(useInMemory: true);
        _service = new HabitService(_db);
    }

    [Fact]
    public async Task AddRelapseAsync_AppendsRelapse()
    {
        var tracker = new HarmfulHabitTracker { HarmfulHabitName = "Smoking", StartDate = DateTime.Today.AddDays(-10) };
        await _service.SaveHarmfulHabitTrackerAsync(tracker);

        await _service.AddRelapseAsync(tracker.Id, DateTime.Today, "slip");

        var reloaded = (await _service.GetHarmfulHabitTrackersAsync()).Single(t => t.Id == tracker.Id);
        reloaded.Relapses.Should().ContainSingle(r => r.Date == DateTime.Today && r.Note == "slip");
    }

    [Fact]
    public async Task AddRelapseAsync_ClampsDateBeforeStartToStart()
    {
        var start = DateTime.Today.AddDays(-5);
        var tracker = new HarmfulHabitTracker { HarmfulHabitName = "Drinking", StartDate = start };
        await _service.SaveHarmfulHabitTrackerAsync(tracker);

        await _service.AddRelapseAsync(tracker.Id, start.AddDays(-3), null);

        var reloaded = (await _service.GetHarmfulHabitTrackersAsync()).Single(t => t.Id == tracker.Id);
        reloaded.Relapses.Single().Date.Should().Be(start);
    }

    public void Dispose() => _db.Dispose();
}
