using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CycleForecastCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 1);

    private static UserProfile Profile(int cycleLength = 28, int periodLength = 5) =>
        new() { CycleLength = cycleLength, PeriodLength = periodLength };

    /// <summary>Period days for episodes starting the given number of days before today.</summary>
    private static List<DateTime> Episodes(int episodeLength, params int[] startsDaysAgo) =>
        startsDaysAgo
            .SelectMany(start => Enumerable.Range(0, episodeLength).Select(i => Today.AddDays(-start + i)))
            .ToList();

    // --- Episode grouping ---

    [Fact]
    public void BuildHistory_ContiguousDays_AreOneEpisode()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 10));

        history.Episodes.Should().ContainSingle();
        history.Episodes[0].Length.Should().Be(4);
        history.Episodes[0].Start.Should().Be(Today.AddDays(-10));
    }

    [Fact]
    public void BuildHistory_OneForgottenDay_StillOneEpisode()
    {
        // Day 3 of the period never got marked; that is a lapse in logging, not a new period.
        var days = new List<DateTime> { Today.AddDays(-10), Today.AddDays(-9), Today.AddDays(-7) };

        CycleForecastCalculator.BuildHistory(days).Episodes.Should().ContainSingle();
    }

    [Fact]
    public void BuildHistory_TwoBlankDays_SplitTheEpisode()
    {
        var days = new List<DateTime> { Today.AddDays(-10), Today.AddDays(-6) };

        CycleForecastCalculator.BuildHistory(days).Episodes.Should().HaveCount(2);
    }

    [Fact]
    public void BuildHistory_DuplicateAndUnorderedDays_AreTolerated()
    {
        var days = new List<DateTime> { Today.AddDays(-9), Today.AddDays(-10), Today.AddDays(-9) };

        var history = CycleForecastCalculator.BuildHistory(days);

        history.Episodes.Should().ContainSingle();
        history.Episodes[0].Length.Should().Be(2);
    }

    // --- Interval plausibility ---

    [Fact]
    public void BuildHistory_ImplausibleInterval_IsDiscardedButTheEpisodeSurvives()
    {
        // A two-month gap in logging would otherwise enter the average as a 62-day cycle.
        var history = CycleForecastCalculator.BuildHistory(Episodes(3, 90, 28, 0));

        history.Episodes.Should().HaveCount(3);
        history.UsableIntervals.Should().Equal(28);
        history.DiscardedIntervals.Should().Be(1);
    }

    [Fact]
    public void BuildHistory_WindowKeepsOnlyTheMostRecentCycles()
    {
        var starts = Enumerable.Range(0, 9).Select(i => i * 28).ToArray();

        var history = CycleForecastCalculator.BuildHistory(Episodes(3, starts));

        history.UsableIntervals.Should().HaveCount(CycleForecastCalculator.HistoryWindow);
    }

    // --- Tiers ---

    [Fact]
    public void Describe_NothingLogged_OffersNoForecast()
    {
        var forecast = CycleForecastCalculator.Describe(CycleHistory.Empty, Profile(), Today, Today);

        forecast.IsAvailable.Should().BeFalse();
        forecast.Basis.Should().Be(CycleForecastBasis.None);
        forecast.PredictedNextStart.Should().BeNull();
    }

    [Fact]
    public void Describe_OneEpisode_FallsBackToTheProfileSetting()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 10));

        var forecast = CycleForecastCalculator.Describe(history, Profile(30), Today, Today);

        forecast.Basis.Should().Be(CycleForecastBasis.ProfileDefault);
        forecast.PredictedNextStart.Should().Be(Today.AddDays(-10).AddDays(30));
        forecast.UncertaintyDays.Should().Be(0, "a setting is not a measurement, so it gets no range");
    }

    [Fact]
    public void Describe_TwoEpisodes_UseTheMeasuredCycleWithAFixedRange()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 30, 2));

        var forecast = CycleForecastCalculator.Describe(history, Profile(), Today, Today);

        forecast.Basis.Should().Be(CycleForecastBasis.SingleCycle);
        forecast.AverageCycleLength.Should().Be(28);
        forecast.UncertaintyDays.Should().Be(CycleForecastCalculator.DefaultUncertaintyDays);
        forecast.IsHighVariability.Should().BeFalse();
    }

    [Fact]
    public void Describe_ThreeEpisodes_AverageAndDeriveTheRangeFromSpread()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 58, 30, 2));

        var forecast = CycleForecastCalculator.Describe(history, Profile(), Today, Today);

        forecast.Basis.Should().Be(CycleForecastBasis.Averaged);
        forecast.RecordedCycleCount.Should().Be(2);
        forecast.AverageCycleLength.Should().Be(28);
        forecast.UncertaintyDays.Should().Be(1, "identical cycles still get the minimum honest range");
    }

    [Fact]
    public void Describe_WildlyVaryingCycles_AreFlaggedAsRough()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 96, 74, 30, 0));

        var forecast = CycleForecastCalculator.Describe(history, Profile(), Today, Today);

        forecast.Basis.Should().Be(CycleForecastBasis.Averaged);
        forecast.IsHighVariability.Should().BeTrue();
        forecast.UncertaintyDays.Should().BeLessThanOrEqualTo(CycleForecastCalculator.MaxUncertaintyDays);
    }

    [Fact]
    public void Describe_MostlyImplausibleHistory_DegradesToTheProfileSetting()
    {
        // Two 60-day gaps and one clean cycle: averaging the survivor alone would look confident and be wrong.
        var history = CycleForecastCalculator.BuildHistory(Episodes(3, 148, 88, 28, 0));

        var forecast = CycleForecastCalculator.Describe(history, Profile(30), Today, Today);

        forecast.Basis.Should().Be(CycleForecastBasis.ProfileDefault);
    }

    // --- The day being described ---

    [Fact]
    public void Describe_CycleDayNeverWraps()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 40));

        var forecast = CycleForecastCalculator.Describe(history, Profile(), Today, Today);

        // The wrap is exactly what someone whose period is late is looking for.
        forecast.CycleDay.Should().Be(41);
    }

    [Fact]
    public void Describe_LatePeriod_IsReportedWithoutRollingThePrediction()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 40));

        var forecast = CycleForecastCalculator.Describe(history, Profile(28), Today, Today);

        forecast.PredictedNextStart.Should().Be(Today.AddDays(-40).AddDays(28));
        forecast.DaysLate.Should().Be(12);
    }

    [Fact]
    public void Describe_MarkedDay_IsAPeriodDay()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 10));

        CycleForecastCalculator.Describe(history, Profile(), Today.AddDays(-9), Today)
            .IsPeriodDay.Should().BeTrue();
    }

    [Fact]
    public void Describe_DayInsideTheNextPredictedPeriod_IsPredictedNotLogged()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 10));
        var predictedStart = Today.AddDays(-10).AddDays(28);

        var forecast = CycleForecastCalculator.Describe(history, Profile(28, 5), predictedStart.AddDays(1), Today);

        forecast.IsPredictedPeriodDay.Should().BeTrue();
        forecast.IsPeriodDay.Should().BeFalse();
    }

    [Fact]
    public void Describe_FertileWindow_SitsTwoWeeksBeforeThePredictedStart()
    {
        var history = CycleForecastCalculator.BuildHistory(Episodes(4, 10));
        var ovulation = Today.AddDays(-10).AddDays(28).AddDays(-14);

        CycleForecastCalculator.Describe(history, Profile(28), ovulation, Today)
            .IsFertileWindowEstimate.Should().BeTrue();
        CycleForecastCalculator.Describe(history, Profile(28), ovulation.AddDays(-8), Today)
            .IsFertileWindowEstimate.Should().BeFalse();
    }
}
