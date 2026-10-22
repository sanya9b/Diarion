using System;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>The season the streak tile draws: whole months, December to February is winter.</summary>
public class SeasonCalendarTests
{
    [Theory]
    [InlineData(1, Season.Winter)]
    [InlineData(2, Season.Winter)]
    [InlineData(3, Season.Spring)]
    [InlineData(5, Season.Spring)]
    [InlineData(6, Season.Summer)]
    [InlineData(8, Season.Summer)]
    [InlineData(9, Season.Autumn)]
    [InlineData(11, Season.Autumn)]
    [InlineData(12, Season.Winter)]
    public void Every_month_falls_in_its_meteorological_season(int month, Season expected)
    {
        SeasonCalendar.Of(new DateTime(2026, month, 15)).Should().Be(expected);
    }

    [Fact]
    public void The_boundaries_are_whole_months()
    {
        SeasonCalendar.Of(new DateTime(2026, 11, 30)).Should().Be(Season.Autumn);
        SeasonCalendar.Of(new DateTime(2026, 12, 1)).Should().Be(Season.Winter);
        SeasonCalendar.Of(new DateTime(2027, 2, 28)).Should().Be(Season.Winter);
        SeasonCalendar.Of(new DateTime(2027, 3, 1)).Should().Be(Season.Spring);
    }
}
