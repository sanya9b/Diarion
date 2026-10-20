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

/// <summary>What <see cref="MoodStatsViewModel"/> does with twenty-nine emotions: reads them at quadrant grain.</summary>
public class MoodStatsQuadrantTests
{
    private static MoodStatsViewModel BuildViewModel(MoodStatistics stats)
    {
        var statsMock = new Mock<IStatisticsService>();
        statsMock.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>())).ReturnsAsync(stats);

        var correlationMock = new Mock<ICorrelationService>();
        correlationMock.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MoodCorrelation>());

        return new MoodStatsViewModel(statsMock.Object, correlationMock.Object);
    }

    [Fact]
    public async Task Donut_GroupsEmotionsByQuadrant_WhileTheTileKeepsTheLeadingEmotion()
    {
        // 3 Happy + 1 Excited = 4 pleasant-energetic; 2 Anxious + 2 Stressed = 4 unpleasant-energetic;
        // 1 Neutral. Nine entries, Happy the single leading emotion at a third.
        var vm = BuildViewModel(new MoodStatistics
        {
            TopEmotion = Emotion.Happy,
            EmotionCounts = new Dictionary<Emotion, int>
            {
                { Emotion.Happy, 3 }, { Emotion.Excited, 1 },
                { Emotion.Anxious, 2 }, { Emotion.Stressed, 2 },
                { Emotion.Neutral, 1 },
                { Emotion.Sad, 0 }
            }
        });

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.EntriesCountText.Should().Be("9");
        vm.EmotionChartData.Select(i => i.Name).Should().Equal(
            EmotionQuadrant.PleasantEnergetic.ToLocalizedName(),
            EmotionQuadrant.UnpleasantEnergetic.ToLocalizedName(),
            EmotionQuadrant.Neutral.ToLocalizedName());
        vm.EmotionChartData.Select(i => i.Percentage).Should().Equal(4.0 / 9, 4.0 / 9, 1.0 / 9);
        vm.EmotionChartData[0].Color.ToArgbHex().Should().EndWith("C26D53");
        vm.EmotionChartData[1].Color.ToArgbHex().Should().EndWith("C9985A");

        vm.TopEmotionText.Should().Be(Emotion.Happy.ToLocalizedName());
        vm.TopEmotionShareText.Should().Contain("33");
    }

    [Fact]
    public async Task Tile_ReportsNoLeader_WhenTwoEmotionsTie_EvenIfTheirQuadrantsDoNot()
    {
        var vm = BuildViewModel(new MoodStatistics
        {
            TopEmotion = Emotion.Happy,
            EmotionCounts = new Dictionary<Emotion, int>
            {
                { Emotion.Happy, 2 }, { Emotion.Proud, 1 }, { Emotion.Sad, 2 }
            }
        });

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.EmotionChartData.Should().HaveCount(2);
        vm.EmotionChartData[0].Percentage.Should().BeApproximately(0.6, 1e-9);
        vm.TopEmotionShareText.Should().BeEmpty();
    }

    [Fact]
    public async Task Heatmap_ColoursByDominantQuadrant_AndFallsBackToTheEmotionsOwn()
    {
        var today = DateTime.Today;
        var vm = BuildViewModel(new MoodStatistics
        {
            TopEmotion = Emotion.Happy,
            EmotionCounts = new Dictionary<Emotion, int> { { Emotion.Happy, 1 } },
            DailyTrend = new List<MoodTrendPoint>
            {
                // A point built by the service: the quadrant wins even where the single most repeated
                // emotion sits elsewhere.
                new() { Date = today.AddDays(-1), HasData = true, DominantEmotion = Emotion.Happy, DominantQuadrant = EmotionQuadrant.UnpleasantCalm },
                // A point without a quadrant (older callers, tests): the emotion's own quadrant.
                new() { Date = today, HasData = true, DominantEmotion = Emotion.Stressed }
            }
        });

        await vm.LoadDataAsync(StatsRange.LastDays(30));

        vm.MoodCalendar.Select(d => d.ColorHex).Should().Equal(
            EmotionQuadrant.UnpleasantCalm.ToColorHex(),
            EmotionQuadrant.UnpleasantEnergetic.ToColorHex());
    }
}
