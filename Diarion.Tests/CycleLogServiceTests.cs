using System;
using System.Threading.Tasks;
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
}
