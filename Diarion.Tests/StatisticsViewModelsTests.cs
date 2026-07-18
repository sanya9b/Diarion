using System;
using System.Collections.Generic;
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
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<int>()))
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
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        // Act
        await vm.LoadDataAsync(30);

        // Assert
        vm.IsEmpty.Should().BeFalse();
        vm.EntriesCountText.Should().Be("10");
        vm.TopEmotionShareText.Should().Contain("60"); // "60%" / "60 %" depending on culture
    }

    [Fact]
    public async Task MoodStats_LoadData_WhenNoEmotions_ResetsKpiText()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<int>()))
            .ReturnsAsync(new MoodStatistics { EmotionCounts = new Dictionary<Emotion, int>() });

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(30);

        vm.IsEmpty.Should().BeTrue();
        vm.EntriesCountText.Should().Be("0");
        vm.TopEmotionShareText.Should().BeEmpty();
    }

    [Fact]
    public async Task MoodStats_LoadData_PopulatesTrendAndHasMoodTrend()
    {
        var today = DateTime.Today;
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<int>()))
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
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        var vm = new MoodStatsViewModel(statsMock.Object, correlationMock.Object);

        await vm.LoadDataAsync(7);

        vm.MoodTrend.Should().HaveCount(3);
        vm.HasMoodTrend.Should().BeTrue(); // two days have data
    }

    [Fact]
    public async Task FinanceStats_LoadData_PositiveBalance_HasLeadingPlus()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<int>()))
            .ReturnsAsync(new FinanceStatistics { TotalIncome = 1000m, TotalExpense = 350.5m });

        var vm = new FinanceStatsViewModel(statsMock.Object);

        await vm.LoadDataAsync(30);

        vm.NetBalance.Should().Be(649.5m);
        vm.NetBalanceText.Should().StartWith("+");
        vm.NetBalanceText.Should().Contain("649");
    }

    [Fact]
    public async Task FinanceStats_LoadData_NegativeBalance_HasLeadingMinus()
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<int>()))
            .ReturnsAsync(new FinanceStatistics { TotalIncome = 100m, TotalExpense = 300m });

        var vm = new FinanceStatsViewModel(statsMock.Object);

        await vm.LoadDataAsync(30);

        vm.NetBalance.Should().Be(-200m);
        vm.NetBalanceText.Should().StartWith("-");
        vm.NetBalanceText.Should().Contain("200");
    }
}
