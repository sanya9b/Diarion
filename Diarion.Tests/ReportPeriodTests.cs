using System;
using System.Linq;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class ReportPeriodTests
{
    // July has 31 days, so a rolling window ending here leaves the last month partial — the case most
    // likely to be read as "spending collapsed this month".
    private static readonly DateTime Today = new(2026, 7, 30);

    private static DateTime Start(int days) => Today.AddDays(-(days - 1));

    [Theory]
    [InlineData(7, ReportBucketUnit.Week)]
    [InlineData(14, ReportBucketUnit.Week)]
    [InlineData(30, ReportBucketUnit.Week)]
    [InlineData(31, ReportBucketUnit.Week)]   // the boundary itself is still weekly
    [InlineData(32, ReportBucketUnit.Month)]
    [InlineData(90, ReportBucketUnit.Month)]
    [InlineData(365, ReportBucketUnit.Month)]
    public void ChooseUnit_SwitchesToMonthsAboveAMonth(int days, ReportBucketUnit expected)
    {
        ReportPeriod.ChooseUnit(days).Should().Be(expected);
    }

    [Fact]
    public void Buckets_Monthly_CoverEveryCalendarMonthInTheWindow()
    {
        var buckets = ReportPeriod.Buckets(Start(90), Today, ReportBucketUnit.Month);

        buckets.Select(b => b.Start.Month).Should().Equal(5, 6, 7);
        buckets[0].Start.Should().Be(new DateTime(2026, 5, 2));   // clipped to the window
        buckets[1].Start.Should().Be(new DateTime(2026, 6, 1));   // whole month
        buckets[1].End.Should().Be(new DateTime(2026, 6, 30));
        buckets[2].End.Should().Be(Today);                        // clipped to today
    }

    [Fact]
    public void Buckets_Monthly_FlagsClippedEdgesAsPartial()
    {
        var buckets = ReportPeriod.Buckets(Start(90), Today, ReportBucketUnit.Month);

        buckets[0].IsPartial.Should().BeTrue();   // starts mid-May
        buckets[1].IsPartial.Should().BeFalse();  // all of June
        buckets[2].IsPartial.Should().BeTrue();   // ends on the 30th of a 31-day month
    }

    [Fact]
    public void Buckets_Weekly_AnchorToTheWindowEndSoTheLastBucketIsWhole()
    {
        // The leftover has to sit on the left. On the right, next to today, a two-day stub bar reads as
        // a collapse in spending rather than as a truncated bucket.
        var buckets = ReportPeriod.Buckets(Start(30), Today, ReportBucketUnit.Week);

        buckets.Should().HaveCount(5);
        buckets.Last().Start.Should().Be(Today.AddDays(-6));
        buckets.Last().End.Should().Be(Today);
        buckets.Last().IsPartial.Should().BeFalse();

        buckets.First().IsPartial.Should().BeTrue();
        buckets.First().Start.Should().Be(Start(30));
    }

    [Fact]
    public void Buckets_AreContiguousAndCoverTheWholeWindowExactlyOnce()
    {
        foreach (var unit in new[] { ReportBucketUnit.Week, ReportBucketUnit.Month })
        {
            var buckets = ReportPeriod.Buckets(Start(180), Today, unit);

            buckets.First().Start.Should().Be(Start(180));
            buckets.Last().End.Should().Be(Today);
            for (var i = 1; i < buckets.Count; i++)
            {
                buckets[i].Start.Should().Be(buckets[i - 1].End.AddDays(1), $"{unit} buckets must not gap or overlap");
            }
        }
    }

    [Fact]
    public void Buckets_SingleDayWindow_IsOneBucket()
    {
        ReportPeriod.Buckets(Today, Today, ReportBucketUnit.Week).Should().ContainSingle();
    }

    [Fact]
    public void Buckets_EndBeforeStart_IsEmpty()
    {
        ReportPeriod.Buckets(Today, Today.AddDays(-5), ReportBucketUnit.Month).Should().BeEmpty();
    }

    [Fact]
    public void PreviousWindow_IsTheSameLengthImmediatelyBefore()
    {
        var (start, end) = ReportPeriod.PreviousWindow(Start(30), Today);

        end.Should().Be(Start(30).AddDays(-1));                 // contiguous
        (end - start).Days.Should().Be(29);                     // 30 days inclusive
        start.Should().Be(new DateTime(2026, 6, 1));
    }

    [Fact]
    public void PreviousWindow_DoesNotOverlapTheCurrentOne()
    {
        var currentStart = Start(7);
        var (_, previousEnd) = ReportPeriod.PreviousWindow(currentStart, Today);

        previousEnd.Should().BeBefore(currentStart);
    }
}
