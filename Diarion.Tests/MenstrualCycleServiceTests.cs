using System;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class MenstrualCycleServiceTests
{
    private readonly MenstrualCycleService _service;

    public MenstrualCycleServiceTests()
    {
        _service = new MenstrualCycleService();
    }

    [Fact]
    public void GetCycleInfoForDate_WhenTrackingDisabled_ReturnsInfoWithDisabledTracking()
    {
        // Arrange
        var profile = new UserProfile { IsMenstrualTrackingEnabled = false };
        var date = DateTime.Today;

        // Act
        var result = _service.GetCycleInfoForDate(date, profile);

        // Assert
        result.IsTrackingEnabled.Should().BeFalse();
        result.Probability.Should().Be(PregnancyProbability.Low);
    }

    [Fact]
    public void GetCycleInfoForDate_WhenTrackingEnabled_ReturnsCorrectCycleDay()
    {
        // Arrange
        var today = DateTime.Today;
        var lastPeriod = today.AddDays(-10); // 11th day of cycle (1-based)
        var profile = new UserProfile
        {
            IsMenstrualTrackingEnabled = true,
            LastPeriodStartDate = lastPeriod,
            CycleLength = 28,
            PeriodLength = 5
        };

        // Act
        var result = _service.GetCycleInfoForDate(today, profile);

        // Assert
        result.IsTrackingEnabled.Should().BeTrue();
        result.DayOfCycle.Should().Be(11);
    }

    [Fact]
    public void GetCycleInfoForDate_WhenInPeriod_ReturnsIsPeriodDayTrue()
    {
        // Arrange
        var today = DateTime.Today;
        var lastPeriod = today.AddDays(-2); // 3rd day of cycle
        var profile = new UserProfile
        {
            IsMenstrualTrackingEnabled = true,
            LastPeriodStartDate = lastPeriod,
            CycleLength = 28,
            PeriodLength = 5
        };

        // Act
        var result = _service.GetCycleInfoForDate(today, profile);

        // Assert
        result.IsPeriodDay.Should().BeTrue();
    }

    [Fact]
    public void GetCycleInfoForDate_WhenInFertileWindow_ReturnsHighProbability()
    {
        // Arrange
        var today = DateTime.Today;
        // Cycle 28 days, ovulation = day 14. Fertile = days 9 to 15.
        var lastPeriod = today.AddDays(-13); // 14th day of cycle
        var profile = new UserProfile
        {
            IsMenstrualTrackingEnabled = true,
            LastPeriodStartDate = lastPeriod,
            CycleLength = 28,
            PeriodLength = 5
        };

        // Act
        var result = _service.GetCycleInfoForDate(today, profile);

        // Assert
        result.IsFertileWindow.Should().BeTrue();
        result.Probability.Should().Be(PregnancyProbability.High);
    }
}