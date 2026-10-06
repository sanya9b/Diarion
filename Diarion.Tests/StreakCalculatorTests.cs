using System;
using System.Collections.Generic;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class StreakCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 15);

    [Fact]
    public void Calculate_NoDates_IsZero()
    {
        StreakCalculator.Calculate(new List<DateTime>(), Today).Should().Be(0);
    }

    [Fact]
    public void Calculate_LastDayBeforeYesterday_IsZero()
    {
        StreakCalculator.Calculate(new[] { Today.AddDays(-2), Today.AddDays(-3) }, Today).Should().Be(0);
    }

    [Fact]
    public void Calculate_TodayOnly_IsOne()
    {
        StreakCalculator.Calculate(new[] { Today }, Today).Should().Be(1);
    }

    [Fact]
    public void Calculate_EndingYesterday_StillCounts()
    {
        // Today isn't over yet, so a chain ending yesterday is not broken.
        StreakCalculator.Calculate(new[] { Today.AddDays(-1), Today.AddDays(-2) }, Today).Should().Be(2);
    }

    [Fact]
    public void Calculate_StopsAtTheFirstGap()
    {
        var dates = new[] { Today, Today.AddDays(-1), Today.AddDays(-3), Today.AddDays(-4) };

        StreakCalculator.Calculate(dates, Today).Should().Be(2);
    }

    [Fact]
    public void Calculate_IgnoresDuplicateDatesAndTimeOfDay()
    {
        var dates = new[]
        {
            Today.AddHours(9),
            Today.AddHours(21),
            Today.AddDays(-1).AddHours(7)
        };

        StreakCalculator.Calculate(dates, Today).Should().Be(2);
    }

    [Fact]
    public void Calculate_AcceptsUnorderedInput()
    {
        var dates = new[] { Today.AddDays(-2), Today, Today.AddDays(-1) };

        StreakCalculator.Calculate(dates, Today).Should().Be(3);
    }

    [Fact]
    public void Calculate_IgnoresFutureDates()
    {
        var dates = new[] { Today.AddDays(3), Today, Today.AddDays(-1) };

        // The debug seeder writes entries days ahead; those must neither extend nor break the streak.
        StreakCalculator.Calculate(dates, Today).Should().Be(2);
    }
}
