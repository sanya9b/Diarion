using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CycleLogServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly CycleLogService _service;

    public CycleLogServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new CycleLogService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task AddEpisodeAsync_RecordsConsecutiveDays()
    {
        var start = DateTime.Today.AddDays(-10);

        await _service.AddEpisodeAsync(start, 4);

        (await _service.GetMarkedDatesAsync())
            .Should().Equal(start, start.AddDays(1), start.AddDays(2), start.AddDays(3));
    }

    [Fact]
    public async Task AddEpisodeAsync_IgnoresTheTimeOfDay()
    {
        await _service.AddEpisodeAsync(DateTime.Today.AddDays(-3).AddHours(21), 1);

        (await _service.GetMarkedDatesAsync()).Should().Equal(DateTime.Today.AddDays(-3));
    }

    [Fact]
    public async Task AddEpisodeAsync_OverlappingRange_DoesNotDuplicateDays()
    {
        var start = DateTime.Today.AddDays(-10);
        await _service.AddEpisodeAsync(start, 4);

        await _service.AddEpisodeAsync(start.AddDays(2), 4);

        (await _service.GetMarkedDatesAsync()).Should().HaveCount(6).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AddEpisodeAsync_TrimsDaysInTheFuture()
    {
        // Recording a period that started yesterday must not claim days that have not happened.
        await _service.AddEpisodeAsync(DateTime.Today.AddDays(-1), 5);

        (await _service.GetMarkedDatesAsync()).Should().Equal(DateTime.Today.AddDays(-1), DateTime.Today);
    }

    [Fact]
    public async Task AddEpisodeAsync_EntirelyInTheFuture_RecordsNothing()
    {
        await _service.AddEpisodeAsync(DateTime.Today.AddDays(3), 5);

        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveEpisodeAsync_DeletesThatEpisodeOnly()
    {
        var older = DateTime.Today.AddDays(-40);
        var newer = DateTime.Today.AddDays(-10);
        await _service.AddEpisodeAsync(older, 4);
        await _service.AddEpisodeAsync(newer, 4);

        await _service.RemoveEpisodeAsync(newer);

        (await _service.GetMarkedDatesAsync())
            .Should().Equal(older, older.AddDays(1), older.AddDays(2), older.AddDays(3));
    }

    [Fact]
    public async Task RemoveEpisodeAsync_FromAnyDayInside_RemovesTheWholeEpisode()
    {
        var start = DateTime.Today.AddDays(-10);
        await _service.AddEpisodeAsync(start, 5);

        await _service.RemoveEpisodeAsync(start.AddDays(3));

        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveEpisodeAsync_UnknownDay_ChangesNothing()
    {
        var start = DateTime.Today.AddDays(-10);
        await _service.AddEpisodeAsync(start, 3);

        await _service.RemoveEpisodeAsync(DateTime.Today.AddDays(-100));

        (await _service.GetMarkedDatesAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMarkedDatesAsync_ReturnsAscendingDates()
    {
        await _service.AddEpisodeAsync(DateTime.Today.AddDays(-2), 2);
        await _service.AddEpisodeAsync(DateTime.Today.AddDays(-40), 2);

        (await _service.GetMarkedDatesAsync()).Should().BeInAscendingOrder();
    }

    // --- Symptom log (Phase B) ---

    private static readonly DateTime Day = new(2026, 7, 10);

    [Fact]
    public async Task SetSymptomsAsync_OnAnUnloggedDay_DoesNotCreateAPeriodDay()
    {
        // The trap the whole feature turns on: symptom rows share the collection with period rows, and
        // counted as periods they would invent episodes and drag every forecast along with them.
        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Cramps });

        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
        (await _service.GetLogsAsync()).Should().ContainSingle()
            .Which.IsSymptomOnly.Should().BeTrue();
    }

    [Fact]
    public async Task SetSymptomsAsync_OnAPeriodDay_KeepsItAPeriodDay()
    {
        await _service.AddEpisodeAsync(Day, 3);

        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Headache });

        (await _service.GetMarkedDatesAsync()).Should().HaveCount(3);
        (await _service.GetLogsAsync()).Single(l => l.Date.Date == Day)
            .IsSymptomOnly.Should().BeFalse();
    }

    [Fact]
    public async Task SetSymptomsAsync_ClearingTheLastSymptom_DropsASymptomOnlyRow()
    {
        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Acne });

        await _service.SetSymptomsAsync(Day, Array.Empty<string>());

        (await _service.GetLogsAsync()).Should().BeEmpty("a symptom-only row with no symptoms has no reason to exist");
    }

    [Fact]
    public async Task SetSymptomsAsync_ClearingSymptomsOnAPeriodDay_KeepsTheDay()
    {
        await _service.AddEpisodeAsync(Day, 2);
        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Fatigue });

        await _service.SetSymptomsAsync(Day, Array.Empty<string>());

        (await _service.GetMarkedDatesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SetSymptomsAsync_ReplacesRatherThanAppends()
    {
        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Cramps, CycleSymptoms.Acne });

        await _service.SetSymptomsAsync(Day, new[] { CycleSymptoms.Bloating });

        (await _service.GetLogsAsync()).Single().Symptoms.Should().Equal(CycleSymptoms.Bloating);
    }

    [Fact]
    public async Task RemoveEpisodeAsync_KeepsTheSymptomsRecordedOnThoseDays()
    {
        await _service.AddEpisodeAsync(Day, 3);
        await _service.SetSymptomsAsync(Day.AddDays(1), new[] { CycleSymptoms.Cramps });

        await _service.RemoveEpisodeAsync(Day);

        // Un-marking a period is a statement about the period, not about how the user felt.
        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
        var remaining = (await _service.GetLogsAsync()).Should().ContainSingle().Subject;
        remaining.Date.Date.Should().Be(Day.AddDays(1));
        remaining.IsSymptomOnly.Should().BeTrue();
        remaining.Symptoms.Should().Equal(CycleSymptoms.Cramps);
    }

    [Fact]
    public void ASymptomFlagLeftUnwrittenMeansAPeriodDay()
    {
        // LiteDB leaves a field it has never seen at its CLR default, so rows written before this one
        // existed read as false. The flag is phrased as the negative for exactly that reason: a bool
        // named IsPeriodDay would have defaulted to false and un-marked the user's entire history.
        new CycleLog().IsSymptomOnly.Should().BeFalse();
    }
}
