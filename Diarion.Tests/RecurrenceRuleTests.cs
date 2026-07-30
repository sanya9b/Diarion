using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Diarion.Models;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class RecurrenceRuleTests
{
    private static DateTime D(int year, int month, int day) => new(year, month, day);

    // --- Anchor and EndDate bounds ---

    [Fact]
    public void IsOccurrenceOn_WithDefaultAnchor_IsTrueForHistoricalDates()
    {
        // The default anchor is DateTime.MinValue, i.e. "no lower bound". Habits have no anchor of their
        // own and lean on this: an anchor defaulting to today would answer false for every past date and
        // silently zero every user's habit strength and streak.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily };

        rule.IsOccurrenceOn(D(1999, 1, 1)).Should().BeTrue();
        rule.Anchor.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void IsOccurrenceOn_BeforeAnchor_IsFalse()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = D(2026, 7, 10) };

        rule.IsOccurrenceOn(D(2026, 7, 9)).Should().BeFalse();
    }

    [Fact]
    public void IsOccurrenceOn_OnAnchor_IsTrue()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = D(2026, 7, 10) };

        rule.IsOccurrenceOn(D(2026, 7, 10)).Should().BeTrue();
    }

    [Fact]
    public void IsOccurrenceOn_OnEndDate_IsTrue()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, EndDate = D(2026, 7, 31) };

        rule.IsOccurrenceOn(D(2026, 7, 31)).Should().BeTrue();
    }

    [Fact]
    public void IsOccurrenceOn_AfterEndDate_IsFalse()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, EndDate = D(2026, 7, 31) };

        rule.IsOccurrenceOn(D(2026, 8, 1)).Should().BeFalse();
    }

    [Fact]
    public void IsOccurrenceOn_IgnoresTheTimeComponent()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = D(2026, 7, 10) };

        rule.IsOccurrenceOn(new DateTime(2026, 7, 10, 23, 59, 0)).Should().BeTrue();
    }

    // --- Weekly ---

    [Fact]
    public void IsOccurrenceOn_Weekly_FiresOnlyOnListedWeekdays()
    {
        // Mon/Wed/Fri. 2026-07-27 is a Monday.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new List<int> { 1, 3, 5 } };

        rule.IsOccurrenceOn(D(2026, 7, 27)).Should().BeTrue();  // Mon
        rule.IsOccurrenceOn(D(2026, 7, 28)).Should().BeFalse(); // Tue
        rule.IsOccurrenceOn(D(2026, 7, 29)).Should().BeTrue();  // Wed
        rule.IsOccurrenceOn(D(2026, 7, 31)).Should().BeTrue();  // Fri
        rule.IsOccurrenceOn(D(2026, 8, 1)).Should().BeFalse();  // Sat
    }

    [Fact]
    public void IsOccurrenceOn_Weekly_WithNoDaysListed_NeverFires()
    {
        // Matches HabitSchedule's behaviour exactly: an empty day list gates everything out.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new List<int>() };

        Enumerable.Range(0, 14)
            .Select(offset => D(2026, 7, 1).AddDays(offset))
            .Should().OnlyContain(day => !rule.IsOccurrenceOn(day));
    }

    // --- IntervalDays ---

    [Fact]
    public void IsOccurrenceOn_IntervalDays_CountsFromTheAnchorInclusive()
    {
        var rule = new RecurrenceRule
        {
            Kind = RecurrenceKind.IntervalDays,
            EveryN = 3,
            Anchor = D(2026, 7, 1)
        };

        rule.IsOccurrenceOn(D(2026, 7, 1)).Should().BeTrue();
        rule.IsOccurrenceOn(D(2026, 7, 2)).Should().BeFalse();
        rule.IsOccurrenceOn(D(2026, 7, 3)).Should().BeFalse();
        rule.IsOccurrenceOn(D(2026, 7, 4)).Should().BeTrue();
        rule.IsOccurrenceOn(D(2026, 7, 7)).Should().BeTrue();
    }

    [Fact]
    public void IsOccurrenceOn_IntervalDays_WithAnAnchorCarryingATime_KeepsThePhase()
    {
        // TimeSpan.Days truncates toward zero, so an un-truncated anchor would shift every occurrence.
        var rule = new RecurrenceRule
        {
            Kind = RecurrenceKind.IntervalDays,
            EveryN = 2,
            Anchor = new DateTime(2026, 7, 1, 14, 30, 0)
        };

        rule.IsOccurrenceOn(D(2026, 7, 1)).Should().BeTrue();
        rule.IsOccurrenceOn(D(2026, 7, 3)).Should().BeTrue();
        rule.IsOccurrenceOn(D(2026, 7, 2)).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void IsOccurrenceOn_IntervalDays_WithNonPositiveStep_BehavesAsDaily(int everyN)
    {
        var rule = new RecurrenceRule
        {
            Kind = RecurrenceKind.IntervalDays,
            EveryN = everyN,
            Anchor = D(2026, 7, 1)
        };

        rule.IsOccurrenceOn(D(2026, 7, 1)).Should().BeTrue();
        rule.IsOccurrenceOn(D(2026, 7, 2)).Should().BeTrue();
    }

    // --- MonthlyByDay ---

    [Theory]
    [InlineData(2026, 4, 30)]  // April has 30 days
    [InlineData(2026, 2, 28)]  // non-leap February
    [InlineData(2028, 2, 29)]  // leap February
    [InlineData(2026, 5, 31)]  // full-length month, no clamping
    public void IsOccurrenceOn_MonthlyByDay_ClampsDownToTheLastDayOfShortMonths(int year, int month, int expectedDay)
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.MonthlyByDay, DayOfMonth = 31 };

        rule.IsOccurrenceOn(D(year, month, expectedDay)).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_MonthlyByDay_InAClampedMonth_YieldsExactlyOneDay()
    {
        // Clamping must not make both the 28th and the 29th fire in a leap February.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.MonthlyByDay, DayOfMonth = 31 };

        rule.Enumerate(D(2028, 2, 1), D(2028, 2, 29))
            .Should().Equal(D(2028, 2, 29));
    }

    [Fact]
    public void Enumerate_MonthlyByDay_FiresOnceInEveryMonthOfAYear()
    {
        // The reason we clamp rather than skip: a bill must not vanish in February.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.MonthlyByDay, DayOfMonth = 31 };

        rule.Enumerate(D(2026, 1, 1), D(2026, 12, 31))
            .Select(d => d.Month)
            .Should().Equal(Enumerable.Range(1, 12));
    }

    // --- Enumerate ---

    [Theory]
    [InlineData(RecurrenceKind.Daily)]
    [InlineData(RecurrenceKind.Weekly)]
    [InlineData(RecurrenceKind.IntervalDays)]
    [InlineData(RecurrenceKind.MonthlyByDay)]
    public void Enumerate_AgreesWithIsOccurrenceOn_OverA400DayWindow(RecurrenceKind kind)
    {
        // Enumerate deliberately has no per-kind fast path. This test is the contract that lets someone
        // add one later without the two answers drifting apart.
        var rule = new RecurrenceRule
        {
            Kind = kind,
            DaysOfWeek = new List<int> { 2, 4 },
            EveryN = 5,
            DayOfMonth = 31,
            Anchor = D(2026, 1, 1)
        };

        var from = D(2026, 1, 1);
        var to = from.AddDays(400);

        var expected = Enumerable.Range(0, 401)
            .Select(offset => from.AddDays(offset))
            .Where(rule.IsOccurrenceOn)
            .ToList();

        rule.Enumerate(from, to).Should().Equal(expected);
    }

    [Fact]
    public void Enumerate_WithAnUnboundedWindow_StopsAtTheSafetyCap()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily };

        var occurrences = rule.Enumerate(D(2026, 1, 1), DateTime.MaxValue).ToList();

        occurrences.Should().HaveCount(3660);
    }

    [Fact]
    public void Enumerate_StartsAtTheAnchorEvenWhenAskedForEarlier()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = D(2026, 7, 10) };

        rule.Enumerate(D(2026, 7, 1), D(2026, 7, 12))
            .Should().Equal(D(2026, 7, 10), D(2026, 7, 11), D(2026, 7, 12));
    }

    [Fact]
    public void Enumerate_StopsAtTheEndDateEvenWhenAskedForLater()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, EndDate = D(2026, 7, 3) };

        rule.Enumerate(D(2026, 7, 1), D(2026, 7, 31))
            .Should().Equal(D(2026, 7, 1), D(2026, 7, 2), D(2026, 7, 3));
    }

    [Fact]
    public void Enumerate_AcrossADaylightSavingBoundary_YieldsOneEntryPerCalendarDay()
    {
        // Dates here are kind-less calendar days, so AddDays is plain arithmetic and never loses or
        // repeats a day at a DST transition. 2026-03-29 is when most of Europe springs forward.
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily };

        rule.Enumerate(D(2026, 3, 28), D(2026, 3, 30))
            .Should().Equal(D(2026, 3, 28), D(2026, 3, 29), D(2026, 3, 30));
    }

    // --- Persistence ---

    [Fact]
    public void Anchor_WhenSetFromAUtcMidnight_StillNamesThatCalendarDay()
    {
        // DateTime.UtcNow is this codebase's timestamp convention, so a UTC-kind value reaching a date
        // field is a live hazard: LiteDB writes UTC and reads back local, which shifts a UTC midnight
        // onto the previous day west of Greenwich. The setter strips the kind to stop that.
        var rule = new RecurrenceRule
        {
            Anchor = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        rule.Anchor.Should().Be(D(2026, 3, 15));
        rule.Anchor.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void RecurrenceRule_RoundTripsThroughLiteDbOnTheSameCalendarDays()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var collection = db.GetCollection<RuleHolder>("holders");

        var original = new RecurrenceRule
        {
            Kind = RecurrenceKind.IntervalDays,
            EveryN = 3,
            DaysOfWeek = new List<int> { 1, 5 },
            DayOfMonth = 17,
            Anchor = D(2026, 3, 15),
            EndDate = D(2026, 12, 31)
        };
        collection.Insert(new RuleHolder { Rule = original });

        var stored = collection.FindAll().Single().Rule;

        stored.Kind.Should().Be(RecurrenceKind.IntervalDays);
        stored.EveryN.Should().Be(3);
        stored.DaysOfWeek.Should().Equal(1, 5);
        stored.DayOfMonth.Should().Be(17);
        stored.Anchor.Date.Should().Be(D(2026, 3, 15));
        stored.EndDate!.Value.Date.Should().Be(D(2026, 12, 31));

        // The phase of an IntervalDays rule is what a shifted anchor would quietly break.
        stored.IsOccurrenceOn(D(2026, 3, 18)).Should().BeTrue();
        stored.IsOccurrenceOn(D(2026, 3, 17)).Should().BeFalse();
    }

    public class RuleHolder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public RecurrenceRule Rule { get; set; } = new();
    }
}
