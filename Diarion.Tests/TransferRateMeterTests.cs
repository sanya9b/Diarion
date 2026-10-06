using System;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class TransferRateMeterTests
{
    [Fact]
    public void Observe_TheFirstReading_HasNothingToDivideBy()
    {
        var meter = new TransferRateMeter();

        meter.Observe(TimeSpan.Zero, 1_000_000).Should().Be(0d);
    }

    [Fact]
    public void Observe_EvenReadings_GiveTheRateBetweenThem()
    {
        var meter = new TransferRateMeter();

        meter.Observe(TimeSpan.Zero, 0);

        meter.Observe(TimeSpan.FromSeconds(1), 2_000_000).Should().BeApproximately(2_000_000d, 1d);
        meter.Observe(TimeSpan.FromSeconds(2), 4_000_000).Should().BeApproximately(2_000_000d, 1d);
    }

    [Fact]
    public void Observe_TwoReadingsInTheSameInstant_DoesNotDivideByZero()
    {
        // A resumed file replays what is already on disk in one go, so this is the ordinary case
        // rather than the exotic one.
        var meter = new TransferRateMeter();
        meter.Observe(TimeSpan.Zero, 0);

        var act = () => meter.Observe(TimeSpan.Zero, 500_000_000);

        // No measurable span, so no claim about speed — rather than an infinity or a crash.
        act.Should().NotThrow().Which.Should().Be(0d);
    }

    [Fact]
    public void Observe_AfterTheLineSpeedsUp_FollowsTheNewSpeedRatherThanTheAverage()
    {
        // The reason the window slides. A rate averaged from the start would still be reporting
        // the first slow minute long after the connection recovered.
        var meter = new TransferRateMeter();

        for (var second = 0; second <= 30; second++)
        {
            meter.Observe(TimeSpan.FromSeconds(second), second * 100_000L);
        }

        double fast = 0;
        for (var second = 31; second <= 60; second++)
        {
            fast = meter.Observe(TimeSpan.FromSeconds(second), 3_000_000L + ((second - 30) * 5_000_000L));
        }

        fast.Should().BeApproximately(5_000_000d, 100_000d);
    }

    [Fact]
    public void Observe_AConnectionSlowerThanTheWindow_StillReportsSomething()
    {
        // One report per thirty seconds outlives a ten-second window. Dropping every expired
        // sample would leave nothing to measure against and the row would read "0 B/s" forever.
        var meter = new TransferRateMeter();
        meter.Observe(TimeSpan.Zero, 0);

        meter.Observe(TimeSpan.FromSeconds(30), 3_000_000)
            .Should().BeApproximately(100_000d, 1d);
    }

    [Fact]
    public void Observe_BytesThatDidNotMove_IsZeroRatherThanNegative()
    {
        var meter = new TransferRateMeter();
        meter.Observe(TimeSpan.Zero, 1_000_000);

        meter.Observe(TimeSpan.FromSeconds(5), 1_000_000).Should().Be(0d);
    }

    [Fact]
    public void Reset_ForgetsTheWindowSoAPauseIsNotReportedAsACollapse()
    {
        var meter = new TransferRateMeter();
        meter.Observe(TimeSpan.Zero, 0);
        meter.Observe(TimeSpan.FromSeconds(1), 2_000_000).Should().BeApproximately(2_000_000d, 1d);

        meter.Reset();

        // First reading after the reset is a baseline again, not a rate measured across the pause.
        meter.Observe(TimeSpan.FromSeconds(11), 2_000_000).Should().Be(0d);
        meter.Observe(TimeSpan.FromSeconds(12), 4_000_000).Should().BeApproximately(2_000_000d, 1d);
    }
}
