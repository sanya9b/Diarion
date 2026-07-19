using System;
using System.Collections.Generic;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class QuitTrackerCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 1);

    [Fact]
    public void CleanSince_NoRelapse_IsStartDate()
    {
        var t = new HarmfulHabitTracker { StartDate = Today.AddDays(-10) };
        QuitTrackerCalculator.CleanSince(t, Today).Should().Be(Today.AddDays(-10));
    }

    [Fact]
    public void CleanSince_WithRelapse_IsLatestRelapse()
    {
        var t = new HarmfulHabitTracker
        {
            StartDate = Today.AddDays(-30),
            Relapses = new List<RelapseEvent>
            {
                new() { Date = Today.AddDays(-20) },
                new() { Date = Today.AddDays(-5) }
            }
        };
        QuitTrackerCalculator.CleanSince(t, Today).Should().Be(Today.AddDays(-5));
    }

    [Fact]
    public void CleanDays_CountsFromCleanSince()
    {
        var t = new HarmfulHabitTracker { StartDate = Today.AddDays(-10) };
        QuitTrackerCalculator.CleanDays(t, Today).Should().Be(10);
    }

    [Fact]
    public void MoneySaved_UsesCostAndUnits()
    {
        var t = new HarmfulHabitTracker { StartDate = Today.AddDays(-10), CostPerUnit = 0.5m, UnitsPerDay = 20 };
        QuitTrackerCalculator.MoneySaved(t, Today).Should().Be(100m); // 10 * 20 * 0.5
    }

    [Fact]
    public void MoneySaved_ZeroWhenNoCost()
    {
        var t = new HarmfulHabitTracker { StartDate = Today.AddDays(-10) };
        QuitTrackerCalculator.MoneySaved(t, Today).Should().Be(0m);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(5, 7)]
    [InlineData(30, 60)]
    public void NextMilestone_ReturnsNextThreshold(int cleanDays, int expected)
    {
        QuitTrackerCalculator.NextMilestone(cleanDays).Should().Be(expected);
    }

    [Fact]
    public void NextMilestone_Null_WhenAllAchieved()
    {
        QuitTrackerCalculator.NextMilestone(400).Should().BeNull();
    }
}
