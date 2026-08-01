using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CycleStatisticsCalculatorTests
{
    private static CycleHistory HistoryFrom(params DateTime[] markedDays) =>
        CycleForecastCalculator.BuildHistory(markedDays.OrderBy(d => d).ToList());

    /// <summary>Four periods four weeks apart, each running five days, all safely in the past.</summary>
    private static CycleHistory RegularHistory(DateTime today)
    {
        var days = new List<DateTime>();
        foreach (var startOffset in new[] { 112, 84, 56, 28 })
        {
            var start = today.AddDays(-startOffset);
            for (int i = 0; i < 5; i++) days.Add(start.AddDays(i));
        }
        return CycleForecastCalculator.BuildHistory(days);
    }

    [Fact]
    public void Compute_WithNoLog_IsEmpty()
    {
        var stats = CycleStatisticsCalculator.Compute(CycleHistory.Empty, new List<CycleLog>(), DateTime.Today);

        stats.IsEmpty.Should().BeTrue();
        stats.Cycles.Should().BeEmpty();
        stats.AverageCycleLength.Should().BeNull();
        stats.RecordedCycleCount.Should().Be(0);
    }

    [Fact]
    public void Compute_WithRegularHistory_ReportsLengthsAndAverages()
    {
        var today = new DateTime(2026, 8, 1);
        var stats = CycleStatisticsCalculator.Compute(RegularHistory(today), new List<CycleLog>(), today);

        stats.IsEmpty.Should().BeFalse();
        stats.Cycles.Should().HaveCount(3);          // four episodes -> three completed cycles
        stats.Cycles.Should().OnlyContain(c => c.Days == 28);
        stats.AverageCycleLength.Should().Be(28);
        stats.ShortestCycle.Should().Be(28);
        stats.LongestCycle.Should().Be(28);
        stats.RecordedCycleCount.Should().Be(3);
        stats.DiscardedCycleCount.Should().Be(0);
        stats.AveragePeriodLength.Should().Be(5);
    }

    [Fact]
    public void Compute_ImplausibleGap_IsChartedButKeptOutOfAverages()
    {
        var today = new DateTime(2026, 8, 1);
        // Two normal cycles, then a 120-day hole from forgotten logging, then another normal one.
        var days = new List<DateTime>();
        foreach (var offset in new[] { 210, 182, 62, 34 })
        {
            var start = today.AddDays(-offset);
            for (int i = 0; i < 4; i++) days.Add(start.AddDays(i));
        }

        var stats = CycleStatisticsCalculator.Compute(
            CycleForecastCalculator.BuildHistory(days), new List<CycleLog>(), today);

        stats.Cycles.Should().HaveCount(3);
        stats.Cycles.Should().Contain(c => !c.IsPlausible, "the 120-day hole is still worth seeing on the chart");
        stats.DiscardedCycleCount.Should().Be(1);
        stats.RecordedCycleCount.Should().Be(2);
        stats.LongestCycle.Should().Be(28, "the implausible gap must not become the longest cycle");
    }

    [Fact]
    public void Compute_PeriodStillRunning_IsLeftOutOfTheAverage()
    {
        var today = new DateTime(2026, 8, 1);

        // A finished 5-day period, then one that started yesterday and is only 2 days in so far.
        var days = new List<DateTime>();
        var finished = today.AddDays(-30);
        for (int i = 0; i < 5; i++) days.Add(finished.AddDays(i));
        days.Add(today.AddDays(-1));
        days.Add(today);

        var stats = CycleStatisticsCalculator.Compute(
            CycleForecastCalculator.BuildHistory(days), new List<CycleLog>(), today);

        stats.AveragePeriodLength.Should().Be(5, "a period that may still be running would drag the mean down");
    }

    [Fact]
    public void Compute_CountsSymptomsAcrossTheWholeLog()
    {
        var today = new DateTime(2026, 8, 1);
        var logs = new List<CycleLog>
        {
            new() { Date = today.AddDays(-3), Symptoms = new List<string> { CycleSymptoms.Cramps, CycleSymptoms.Fatigue } },
            new() { Date = today.AddDays(-2), Symptoms = new List<string> { CycleSymptoms.Cramps } },
            new() { Date = today.AddDays(-1), Symptoms = new List<string>() },
            new() { Date = today, Symptoms = new List<string> { CycleSymptoms.Cramps, CycleSymptoms.Headache } }
        };

        var stats = CycleStatisticsCalculator.Compute(HistoryFrom(today), logs, today);

        stats.Symptoms.Should().HaveCount(3);
        stats.Symptoms[0].Key.Should().Be(CycleSymptoms.Cramps);
        stats.Symptoms[0].Count.Should().Be(3);
        stats.Symptoms.Should().BeInDescendingOrder(s => s.Count);
    }

    [Fact]
    public void Compute_ChartIsCappedToTheMostRecentCycles()
    {
        var today = new DateTime(2026, 8, 1);
        var days = new List<DateTime>();
        for (int cycle = 20; cycle >= 1; cycle--)
        {
            days.Add(today.AddDays(-28 * cycle));
        }

        var stats = CycleStatisticsCalculator.Compute(
            CycleForecastCalculator.BuildHistory(days), new List<CycleLog>(), today);

        stats.Cycles.Should().HaveCount(CycleStatisticsCalculator.MaxChartedCycles);
        stats.RecordedCycleCount.Should().Be(19, "the averages still stand on every cycle, not just the charted ones");
        stats.Cycles.Last().Start.Should().Be(today.AddDays(-56), "the chart keeps the most recent cycles");
    }
}
