using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class StreakWalkerTests
{
    private static readonly DateTime Today = new(2026, 7, 15); // a Wednesday

    private static DateTime[] Days(params int[] daysAgo) =>
        daysAgo.Select(d => Today.AddDays(-d)).ToArray();

    [Fact]
    public void Walk_EmptySet_ReturnsNone()
    {
        StreakWalker.Walk(Array.Empty<DateTime>(), Today, 3).Should().Be(StreakResult.None);
    }

    [Fact]
    public void Walk_NoGrace_CountsConsecutiveRun()
    {
        var result = StreakWalker.Walk(Days(0, 1, 2), Today, 0);

        result.Length.Should().Be(3);
        result.GraceUsed.Should().Be(0);
        result.HeldByGrace.Should().BeFalse();
    }

    [Fact]
    public void Walk_NoGrace_SingleGap_EndsRun()
    {
        StreakWalker.Walk(Days(0, 2, 3), Today, 0).Length.Should().Be(1);
    }

    [Fact]
    public void Walk_TodayNotLogged_DoesNotConsumeGrace()
    {
        // Today is still in progress; it is not a miss you can be charged for.
        var result = StreakWalker.Walk(Days(1, 2), Today, 1);

        result.Length.Should().Be(2);
        result.GraceUsed.Should().Be(0);
    }

    [Fact]
    public void Walk_SingleMissedDay_IsForgiven()
    {
        var result = StreakWalker.Walk(Days(0, 2, 3), Today, 1);

        result.Length.Should().Be(3);
        result.GraceUsed.Should().Be(1);
        result.HeldByGrace.Should().BeTrue();
    }

    [Fact]
    public void Walk_GraceExhausted_EndsRun()
    {
        // Missing day 1 spends the only unit; missing day 3 has nothing left to spend.
        StreakWalker.Walk(Days(0, 2, 4, 5), Today, 1).Length.Should().Be(2);
    }

    [Fact]
    public void Walk_GraceIsBudgetPerRun_NotPerGap()
    {
        // Two separate one-day gaps still share one budget.
        StreakWalker.Walk(Days(0, 2, 4), Today, 1).Length.Should().Be(2);
    }

    [Fact]
    public void Walk_CountsLoggedDaysNotCalendarDays()
    {
        // Five calendar days are spanned, but only four were written on.
        StreakWalker.Walk(Days(0, 1, 3, 4), Today, 2).Length.Should().Be(4);
    }

    [Fact]
    public void Walk_ReportsOnlyTheGraceInsideTheRun()
    {
        // One forgiven gap inside a run of three; the unspent budget is not reported as spent.
        StreakWalker.Walk(Days(0, 2), Today, 3).GraceUsed.Should().Be(1);
    }

    [Fact]
    public void Walk_GraceBurnedPastTheBreak_IsNotReportedAsUsed()
    {
        // Days 1-3 are missing, so the budget runs out and the run ends at today. Those two units held
        // nothing up — reporting them would make a pristine one-day streak look nearly broken.
        var result = StreakWalker.Walk(Days(0, 4), Today, 2);

        result.Length.Should().Be(1);
        result.GraceUsed.Should().Be(0);
        result.HeldByGrace.Should().BeFalse();
    }

    [Fact]
    public void Walk_FutureDates_Ignored()
    {
        var dates = new[] { Today.AddDays(5), Today, Today.AddDays(-1) };

        StreakWalker.Walk(dates, Today, 0).Length.Should().Be(2);
    }

    [Fact]
    public void Walk_NonScheduledDay_DoesNotConsumeGrace()
    {
        // Mon/Wed/Fri. Today is Wednesday; Wed and the preceding Mon are done, Tuesday was never due.
        var schedule = MonWedFri();
        var done = new[] { Today, Today.AddDays(-2) };

        StreakWalker.Walk(done, Today, 0, schedule.IsOccurrenceOn).Length.Should().Be(2);
    }

    [Fact]
    public void Walk_SkippedScheduledDay_ConsumesGrace()
    {
        // Mon/Wed/Fri with the Monday in between missed: a scheduled day, so it costs a unit.
        var schedule = MonWedFri();
        var done = new[] { Today, Today.AddDays(-5) }; // Wed and the previous Friday

        var result = StreakWalker.Walk(done, Today, 1, schedule.IsOccurrenceOn);

        result.Length.Should().Be(2);
        result.GraceUsed.Should().Be(1);
    }

    private static RecurrenceRule MonWedFri() => new()
    {
        Kind = RecurrenceKind.Weekly,
        DaysOfWeek = new List<int> { (int)DayOfWeek.Monday, (int)DayOfWeek.Wednesday, (int)DayOfWeek.Friday }
    };
}
