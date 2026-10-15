using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The energy axis <see cref="StatisticsService"/> reads alongside valence (spec 18, phase 3).</summary>
public class StatisticsEnergyTests
{
    private static StatisticsService Service(params DiaryEntryStatsDto[] rows)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(rows.ToList());

        return new StatisticsService(diary.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);
    }

    private static List<HourMood> Hours(params (int Hour, Emotion Mood)[] entries) =>
        entries.Select(e => new HourMood { Hour = e.Hour, Mood = e.Mood }).ToList();

    [Fact]
    public async Task HourlyProfile_AveragesEnergyFromTheSameObservationsAsValence()
    {
        // Hour 9: Angry (-2, +2) and Tired (-1, -2) → valence -1.5, energy exactly 0.
        // Hour 10: Excited (+2, +2) alone → energy +2.
        var today = DateTime.Today;
        var service = Service(new DiaryEntryStatsDto
        {
            Date = today,
            HourlyMood = Hours((9, Emotion.Angry), (9, Emotion.Tired), (10, Emotion.Excited))
        });

        var result = await service.GetMoodStatisticsAsync(StatsRange.LastDays(7));

        var hour9 = result.HourlyProfile.Single(p => p.Hour == 9);
        hour9.Valence.Should().Be(-1.5);
        hour9.Energy.Should().Be(0);
        hour9.HasData.Should().BeTrue("cancelling out is a reading, not an absence");
        hour9.Count.Should().Be(2);

        var hour10 = result.HourlyProfile.Single(p => p.Hour == 10);
        hour10.Energy.Should().Be(2);
        hour10.Valence.Should().Be(2);
    }

    [Fact]
    public async Task HourlyProfile_UnloggedHour_HasNoEnergyEither()
    {
        var service = Service(new DiaryEntryStatsDto { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.Calm)) });

        var result = await service.GetMoodStatisticsAsync(StatsRange.LastDays(7));

        var hour14 = result.HourlyProfile.Single(p => p.Hour == 14);
        hour14.HasData.Should().BeFalse();
        hour14.Energy.Should().Be(0);
    }

    [Fact]
    public async Task DailyTrend_CarriesEnergy_WithTheScalarFallbackValenceHas()
    {
        var today = DateTime.Today;
        var service = Service(
            // Hourly wins: Relaxed (-2) and Energetic (+2) → 0, whatever the scalar says.
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Happy, HourlyMood = Hours((9, Emotion.Relaxed), (15, Emotion.Energetic)) },
            // Scalar only: Peaceful sits at -2 on the energy axis.
            new DiaryEntryStatsDto { Date = today, Emotion = Emotion.Peaceful });

        var result = await service.GetMoodStatisticsAsync(StatsRange.LastDays(3));

        result.DailyTrend.Should().HaveCount(3);
        result.DailyTrend.Single(p => p.Date == today.AddDays(-2)).Should().Match<MoodTrendPoint>(p => !p.HasData && p.Energy == 0);
        result.DailyTrend.Single(p => p.Date == today.AddDays(-1)).Energy.Should().Be(0);
        result.DailyTrend.Single(p => p.Date == today).Energy.Should().Be(-2);
        result.DailyTrend.Single(p => p.Date == today).Valence.Should().Be(1);
    }
}
