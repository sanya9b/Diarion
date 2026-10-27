using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class StatisticsViewModelsTests
{
    [Fact]
    public async Task MoodStats_LoadData_ComputesEntriesCountAndTopShare()
    {
        // Arrange: 6 Happy + 4 Calm = 10 entries; top emotion share = 60%.
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new MoodStatistics
            {
                TopEmotion = Emotion.Happy,
                EmotionCounts = new Dictionary<Emotion, int>
                {
                    { Emotion.Happy, 6 },
                    { Emotion.Calm, 4 }
                }
            });

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        // Act
        await vm.LoadDataAsync(StatsRange.LastDays(30));

        // Assert
        vm.IsEmpty.Should().BeFalse();
        vm.EntriesCountText.Should().Be("10");
        vm.TopEmotionShareText.Should().Contain("60"); // "60%" / "60 %" depending on culture
    }

    [Fact]
    public async Task MoodStats_LoadData_WhenNoEmotions_ResetsKpiText()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new MoodStatistics { EmotionCounts = new Dictionary<Emotion, int>() });

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.IsEmpty.Should().BeTrue();
        vm.EntriesCountText.Should().Be("0");
        vm.TopEmotionShareText.Should().BeEmpty();
    }

    [Fact]
    public async Task MoodStats_LoadData_PopulatesTrendAndHasMoodTrend()
    {
        var today = DateTime.Today;
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new MoodStatistics
            {
                TopEmotion = Emotion.Happy,
                EmotionCounts = new Dictionary<Emotion, int> { { Emotion.Happy, 2 }, { Emotion.Sad, 1 } },
                DailyTrend = new List<MoodTrendPoint>
                {
                    new MoodTrendPoint { Date = today.AddDays(-2), Valence = 2, HasData = true },
                    new MoodTrendPoint { Date = today.AddDays(-1), Valence = 0, HasData = false },
                    new MoodTrendPoint { Date = today, Valence = -2, HasData = true }
                }
            });

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(7));

        vm.MoodTrend.Should().HaveCount(3);
        vm.HasMoodTrend.Should().BeTrue(); // two days have data

        // Sparkline mirrors the trend, with gaps mapped to null.
        vm.MoodSparkline.Should().HaveCount(3);
        vm.MoodSparkline.Should().Equal(new double?[] { 2, null, -2 });

        // A 3-day window is too short for the heatmap.
        vm.HasMoodCalendar.Should().BeFalse();
    }

    [Fact]
    public async Task MoodStats_LoadData_PopulatesHeatmapForLongWindow()
    {
        var today = DateTime.Today;

        var trend = new List<MoodTrendPoint>();
        for (int i = 29; i >= 0; i--)
        {
            bool has = i % 2 == 0;
            trend.Add(new MoodTrendPoint
            {
                Date = today.AddDays(-i),
                Valence = has ? 2 : 0,
                HasData = has,
                DominantEmotion = has ? Emotion.Happy : Emotion.None
            });
        }

        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new MoodStatistics
            {
                TopEmotion = Emotion.Happy,
                EmotionCounts = new Dictionary<Emotion, int> { { Emotion.Happy, 15 } },
                DailyTrend = trend
            });

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.MoodCalendar.Should().HaveCount(30);
        vm.HasMoodCalendar.Should().BeTrue();
        vm.MoodCalendar.First(c => c.HasData).ColorHex.Should().Be("#C26D53"); // Happy = Coral
    }

    [Fact]
    public async Task SleepStats_LoadData_BuildsDurationAndQualitySparklines()
    {
        var today = DateTime.Today;
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetSleepStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new SleepStatistics
            {
                AverageSleepDuration = TimeSpan.FromHours(7),
                AverageSleepQuality = 8,
                DailyData = new List<SleepDataPoint>
                {
                    new SleepDataPoint { Date = today.AddDays(-2), Duration = TimeSpan.FromHours(8), Quality = 9 },
                    new SleepDataPoint { Date = today.AddDays(-1), Duration = TimeSpan.Zero, Quality = 0 }, // gap
                    new SleepDataPoint { Date = today, Duration = TimeSpan.FromHours(6), Quality = 7 }
                }
            });

        var vm = new SleepStatsViewModel(statsMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(7));

        vm.IsEmpty.Should().BeFalse();
        vm.DurationSparkline.Should().Equal(new double?[] { 8, null, 6 });
        vm.QualitySparkline.Should().Equal(new double?[] { 9, null, 7 });
    }

    [Fact]
    public async Task FinanceStats_LoadData_PositiveBalance_HasLeadingPlus()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<StatsRange>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new FinanceStatistics { TotalIncome = 1000m, TotalExpense = 350.5m });

        var vm = new FinanceStatsViewModel(statsMock.Object, TestProfiles.Service());

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.NetBalance.Should().Be(649.5m);
        vm.NetBalanceText.Should().StartWith("+");
        vm.NetBalanceText.Should().Contain("649");
    }

    [Fact]
    public async Task FinanceStats_LoadData_NegativeBalance_HasLeadingMinus()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<StatsRange>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new FinanceStatistics { TotalIncome = 100m, TotalExpense = 300m });

        var vm = new FinanceStatsViewModel(statsMock.Object, TestProfiles.Service());

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.NetBalance.Should().Be(-200m);
        vm.NetBalanceText.Should().StartWith("-");
        vm.NetBalanceText.Should().Contain("200");
    }

    [Fact]
    public async Task MoodStats_EnergyCard_ShowsOnlyHoldingEnergyFindings_AndCountsItsOwnFamily()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
            .ReturnsAsync(new MoodStatistics
            {
                TopEmotion = Emotion.Happy,
                EmotionCounts = new Dictionary<Emotion, int> { { Emotion.Happy, 15 } }
            });

        var correlationMock = new Mock<ICorrelationService>();
        // Mood pass: eight tried, none held — so the mood card gives way to "nothing holds yet".
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(Enumerable.Range(0, 8).Select(i => new MoodCorrelation
            {
                FactorKey = CorrelationService.Factors.SleepDuration, Confidence = 1, Coefficient = 0.1, SampleSize = 20
            }).ToList());
        // Energy pass: three tried, one held.
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>
            {
                new() { FactorKey = CorrelationService.Factors.SleepDuration, Axis = MoodAxis.Energy, Confidence = 4, Coefficient = 0.6, SampleSize = 20 },
                new() { FactorKey = CorrelationService.Factors.MealsLogged, Axis = MoodAxis.Energy, Confidence = 2, Coefficient = -0.2, SampleSize = 20 },
                new() { FactorKey = CorrelationService.Factors.TaskCompletion, Axis = MoodAxis.Energy, Confidence = 1, Coefficient = 0.05, SampleSize = 20 }
            });

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.HasCorrelations.Should().BeFalse();
        vm.HasInsightProgress.Should().BeTrue();

        vm.HasEnergyCorrelations.Should().BeTrue();
        vm.EnergyCorrelations.Should().ContainSingle();
        vm.EnergyCorrelations[0].Description.Should().StartWith("↑");
        vm.EnergyCorrelations[0].Dots.Should().Be("●●●●○");
        vm.EnergyCorrelationSummaryText.Should().Contain("3").And.Contain("1");
    }

    [Fact]
    public async Task MoodStats_EnergyCard_IsAbsent_WhenNothingAboutEnergyHolds_AndClearsOnEmptyPeriod()
    {
        var stats = new MoodStatistics
        {
            TopEmotion = Emotion.Happy,
            EmotionCounts = new Dictionary<Emotion, int> { { Emotion.Happy, 15 } }
        };
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>())).ReturnsAsync(() => stats);

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>
            {
                new() { FactorKey = CorrelationService.Factors.SleepDuration, Confidence = 5, Coefficient = 0.8, SampleSize = 20 }
            });
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>
            {
                new() { FactorKey = CorrelationService.Factors.SleepDuration, Axis = MoodAxis.Energy, Confidence = 2, Coefficient = 0.3, SampleSize = 20 }
            });

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.HasCorrelations.Should().BeTrue();
        vm.HasEnergyCorrelations.Should().BeFalse();
        vm.EnergyCorrelationSummaryText.Should().BeEmpty();

        // A period with no emotions at all resets the energy card along with the rest.
        stats = new MoodStatistics { EmotionCounts = new Dictionary<Emotion, int>() };
        correlationMock.Setup(c => c.GetEnergyCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>
            {
                new() { FactorKey = CorrelationService.Factors.SleepDuration, Axis = MoodAxis.Energy, Confidence = 5, Coefficient = 0.9, SampleSize = 20 }
            });

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.IsEmpty.Should().BeTrue();
        vm.HasEnergyCorrelations.Should().BeFalse();
    }
}
