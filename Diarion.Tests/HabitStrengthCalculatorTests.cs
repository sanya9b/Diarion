using System;
using System.Collections.Generic;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class HabitStrengthCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 1); // Wednesday

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

    [Fact]
    public void Strength_SpecificDays_AllScheduledDone_IsHigh_NotPenalizedForOtherDays()
    {
        var schedule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new() { 1, 3, 5 } };
        var from = Today.AddDays(-179);

        var set = new HashSet<DateTime>();
        for (var d = from; d <= Today; d = d.AddDays(1))
        {
            if (schedule.IsOccurrenceOn(d)) set.Add(d); // only scheduled days done
        }

        // ~77 scheduled EMA steps over 180 days converge to ~0.83 — well above the ~0.43 a
        // schedule-blind calc would give (which would treat every skipped Tue/Thu/weekend as a miss).
        HabitStrengthCalculator.Strength(set, from, Today, schedule).Should().BeGreaterThan(75);
    }

    [Fact]
    public void CurrentStreak_SpecificDays_WalksScheduledDaysOnly()
    {
        // Mon/Wed/Fri; the last three scheduled days on/before Wed 2026-07-01 are Jul 1, Jun 29, Jun 26.
        var schedule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new() { 1, 3, 5 } };
        var set = new HashSet<DateTime>
        {
            new(2026, 7, 1),  // Wed
            new(2026, 6, 29), // Mon
            new(2026, 6, 26)  // Fri
        };

        HabitStrengthCalculator.CurrentStreak(set, Today, schedule).Should().Be(3);
    }

    [Fact]
    public void Strength_TimesPerWeek_MeetingTargetEveryWeek_IsHigh()
    {
        var target = new CompletionTarget { TimesPerWeek = 3 };
        var from = Today.AddDays(-181); // ~26 weeks

        var set = new HashSet<DateTime>();
        for (var d = from; d <= Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday) set.Add(d); // 3/week
        }

        HabitStrengthCalculator.Strength(set, from, Today, null, target).Should().BeGreaterThan(90);
    }

    [Fact]
    public void CurrentStreak_TimesPerWeek_CountsConsecutiveWeeksMeetingTarget()
    {
        var target = new CompletionTarget { TimesPerWeek = 2 };
        var set = new HashSet<DateTime>
        {
            new(2026, 6, 29), new(2026, 6, 30), // current week (Mon Jun29–): 2 -> meets
            new(2026, 6, 22), new(2026, 6, 24), // previous week: 2 -> meets
            new(2026, 6, 15)                     // week before: 1 -> misses, streak stops
        };

        HabitStrengthCalculator.CurrentStreak(set, Today, null, target, graceDays: 0).Should().Be(2);
    }

    [Fact]
    public void CurrentStreak_WithGrace_ForgivesMissedScheduledDay()
    {
        var set = new HashSet<DateTime> { Today, Today.AddDays(-2), Today.AddDays(-3) };

        HabitStrengthCalculator.CurrentStreak(set, Today, null, null, graceDays: 0).Should().Be(1);
        HabitStrengthCalculator.CurrentStreak(set, Today, null, null, graceDays: 1).Should().Be(3);
    }

    [Fact]
    public void CurrentStreak_WithGrace_NonScheduledDaysDoNotConsumeQuota()
    {
        // Mon/Wed/Fri. Today is Wednesday; Mon and the previous Fri are done, so the run is unbroken and
        // the quota must still be untouched when the Monday before that is missed.
        var schedule = new RecurrenceRule
        {
            Kind = RecurrenceKind.Weekly,
            DaysOfWeek = new List<int> { (int)DayOfWeek.Monday, (int)DayOfWeek.Wednesday, (int)DayOfWeek.Friday }
        };
        var set = new HashSet<DateTime> { Today, Today.AddDays(-2), Today.AddDays(-5) };

        HabitStrengthCalculator.CurrentStreak(set, Today, schedule, null, graceDays: 0).Should().Be(3);
    }

    [Fact]
    public void CurrentStreak_TimesPerWeek_IgnoresGrace()
    {
        var target = new CompletionTarget { TimesPerWeek = 2 };
        var set = new HashSet<DateTime>
        {
            new(2026, 6, 29), new(2026, 6, 30),
            new(2026, 6, 22), new(2026, 6, 24),
            new(2026, 6, 15)
        };

        // Weeks, not days — a day-granular quota has nothing to forgive here.
        HabitStrengthCalculator.CurrentStreak(set, Today, null, target, graceDays: 3).Should().Be(2);
    }
}
