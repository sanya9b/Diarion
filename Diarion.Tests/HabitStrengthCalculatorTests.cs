using System;
using System.Collections.Generic;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class HabitStrengthCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 1);

    [Fact]
    public void Strength_EveryDayDone_OverFullWindow_IsHigh()
    {
        var from = Today.AddDays(-179);
        var set = new HashSet<DateTime>();
        for (var d = from; d <= Today; d = d.AddDays(1)) set.Add(d);

        HabitStrengthCalculator.Strength(set, from, Today).Should().BeGreaterThan(90);
    }

    [Fact]
    public void Strength_NoDays_IsZero()
    {
        HabitStrengthCalculator.Strength(new HashSet<DateTime>(), Today.AddDays(-60), Today).Should().Be(0);
    }

    [Fact]
    public void Strength_RecentCompletions_BeatOldCompletions()
    {
        var from = Today.AddDays(-59);

        var recent = new HashSet<DateTime>();
        for (int i = 0; i < 15; i++) recent.Add(Today.AddDays(-i));

        var old = new HashSet<DateTime>();
        for (int i = 40; i < 55; i++) old.Add(Today.AddDays(-i));

        var sRecent = HabitStrengthCalculator.Strength(recent, from, Today);
        var sOld = HabitStrengthCalculator.Strength(old, from, Today);

        sRecent.Should().BeGreaterThan(sOld);
    }

    [Fact]
    public void CurrentStreak_ConsecutiveEndingToday()
    {
        var set = new HashSet<DateTime> { Today, Today.AddDays(-1), Today.AddDays(-2) };
        HabitStrengthCalculator.CurrentStreak(set, Today).Should().Be(3);
    }

    [Fact]
    public void CurrentStreak_TodayUnfinishedButYesterdayDone_CountsFromYesterday()
    {
        var set = new HashSet<DateTime> { Today.AddDays(-1), Today.AddDays(-2) };
        HabitStrengthCalculator.CurrentStreak(set, Today).Should().Be(2);
    }

    [Fact]
    public void CurrentStreak_Broken_IsZero()
    {
        var set = new HashSet<DateTime> { Today.AddDays(-3), Today.AddDays(-4) };
        HabitStrengthCalculator.CurrentStreak(set, Today).Should().Be(0);
    }
}
