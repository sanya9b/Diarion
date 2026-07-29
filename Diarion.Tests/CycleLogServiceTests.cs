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
    public async Task ToggleAsync_MarksThenUnmarks()
    {
        var day = DateTime.Today;

        (await _service.ToggleAsync(day)).Should().BeTrue();
        (await _service.GetMarkedDatesAsync()).Should().Equal(day);

        (await _service.ToggleAsync(day)).Should().BeFalse();
        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleAsync_IgnoresTheTimeOfDay()
    {
        await _service.ToggleAsync(DateTime.Today.AddHours(9));

        var marked = await _service.GetMarkedDatesAsync();

        marked.Should().Equal(DateTime.Today);
    }

    [Fact]
    public async Task ToggleAsync_RetroactiveDay_IsAccepted()
    {
        var pastDay = DateTime.Today.AddDays(-20);

        (await _service.ToggleAsync(pastDay)).Should().BeTrue();
        (await _service.GetMarkedDatesAsync()).Should().Contain(pastDay);
    }

    [Fact]
    public async Task ToggleAsync_FutureDay_IsRejected()
    {
        // A period cannot be reported before it happens, and the forecast anchors on the newest episode.
        (await _service.ToggleAsync(DateTime.Today.AddDays(1))).Should().BeFalse();
        (await _service.GetMarkedDatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMarkedDatesAsync_ReturnsAscendingDates()
    {
        await _service.ToggleAsync(DateTime.Today);
        await _service.ToggleAsync(DateTime.Today.AddDays(-30));
        await _service.ToggleAsync(DateTime.Today.AddDays(-2));

        (await _service.GetMarkedDatesAsync()).Should().BeInAscendingOrder();
    }
}
