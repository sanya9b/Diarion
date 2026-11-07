using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class CorrelationServiceTests
{
    private static DiaryEntryStatsDto Entry(int daysAgo, double? sleepHours = null, Emotion emotion = Emotion.None, int quality = 0)
    {
        var dto = new DiaryEntryStatsDto
        {
            Date = DateTime.Today.AddDays(-daysAgo),
            Emotion = emotion,
            SleepQuality = quality
        };
        if (sleepHours.HasValue)
        {
            dto.SleepStart = new TimeSpan(22, 0, 0);
            dto.SleepEnd = TimeSpan.FromHours((22 + sleepHours.Value) % 24);
        }
        return dto;
    }

    private static Emotion Tier(int i) => i switch
    {
        < 5 => Emotion.Sad,      // -2
        < 10 => Emotion.Anxious, // -1
        < 15 => Emotion.Calm,    // +1
        _ => Emotion.Happy       // +2
    };

    /// <summary>Cycle tracking off by default, so these cases keep measuring sleep alone.</summary>
    private static CorrelationService Build(
        IEnumerable<DiaryEntryStatsDto> data,
        IEnumerable<CycleLog>? cycleLogs = null,
        bool cycleTracking = false)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(data.ToList());

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync((cycleLogs ?? Enumerable.Empty<CycleLog>()).ToList());

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile
        {
            Gender = GenderType.Female,
            IsMenstrualTrackingEnabled = cycleTracking
        });

        // Tasks and spending are not what these tests are about, but Moq hands back a null Task
        // for an unconfigured async member, so both need an explicit empty result.
        var todos = new Mock<ITodoService>();
        todos.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(Enumerable.Empty<TodoStatsDto>());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FinanceTransaction>());

        return new CorrelationService(
            diary.Object, cycle.Object, profile.Object, todos.Object, finance.Object);
    }

    [Fact]
    public async Task StrongPositive_SleepDurationVsMood_IsDetected()
    {
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 20; i++)
        {
            data.Add(Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: Tier(i)));
        }
        var service = Build(data);

        var result = await service.GetMoodCorrelationsAsync(StatsRange.LastDays(20));

        result.Should().ContainSingle();
        var sleep = result[0];
        sleep.FactorKey.Should().Be("SleepDuration");
        sleep.Coefficient.Should().BeGreaterThan(0.7);
        sleep.Strength.Should().Be(CorrelationStrength.Strong);
        sleep.Confidence.Should().Be(5);
        sleep.SampleSize.Should().Be(20);
        sleep.LagDays.Should().Be(0);
    }

    [Fact]
    public async Task BelowMinimumSampleSize_IsExcluded()
    {
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 10; i++) // < MinSampleSize (14)
        {
            data.Add(Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: Tier(i)));
        }
        var service = Build(data);

        var result = await service.GetMoodCorrelationsAsync(StatsRange.LastDays(10));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NoMoodVariance_YieldsZeroCoefficient()
    {
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 16; i++)
        {
            data.Add(Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: Emotion.Happy)); // constant mood
        }
        var service = Build(data);

        var result = await service.GetMoodCorrelationsAsync(StatsRange.LastDays(16));

        result.Should().ContainSingle();
        result[0].Coefficient.Should().Be(0);
        result[0].Strength.Should().Be(CorrelationStrength.Negligible);
    }

    [Fact]
    public async Task LaggedCorrelation_YesterdaySleepVsTodayMood_IsDetected()
    {
        // sleep on day D correlates with mood on day D+1 (i.e. mood at daysAgo=K-1 tracks sleep at daysAgo=K)
        var data = new List<DiaryEntryStatsDto>();
        for (int k = 1; k <= 20; k++)
        {
            data.Add(Entry(daysAgo: k, sleepHours: 4 + k * 0.2)); // factor day (no mood)
        }
        for (int j = 0; j <= 19; j++)
        {
            var sleepOfPreviousDay = 4 + (j + 1) * 0.2;
            var tier = sleepOfPreviousDay switch
            {
                < 5 => Emotion.Sad,
                < 6 => Emotion.Anxious,
                < 7 => Emotion.Calm,
                _ => Emotion.Happy
            };
            data.Add(Entry(daysAgo: j, emotion: tier)); // mood day (no sleep)
        }
        var service = Build(data);

        var lagged = await service.GetMoodCorrelationsAsync(StatsRange.LastDays(20), lagDays: 1);

        lagged.Should().ContainSingle();
        lagged[0].FactorKey.Should().Be("SleepDuration");
        lagged[0].LagDays.Should().Be(1);
        Math.Abs(lagged[0].Coefficient).Should().BeGreaterThan(0.7);
    }

    // ---- which factor the readiness names (spec 17, R1.1) ----------------------------------

    [Fact]
    public async Task Readiness_WithOneFactor_NamesIt()
    {
        var data = Enumerable.Range(0, 9).Select(i => Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: Tier(i)));

        var readiness = await Build(data).GetReadinessAsync(StatsRange.LastDays(30));

        readiness.PairedDays.Should().Be(9);
        readiness.BestFactorKey.Should().Be(CorrelationService.Factors.SleepDuration);
    }

    [Fact]
    public async Task Readiness_WithTwoFactors_NamesTheOneWithMorePairedDays()
    {
        // Sleep quality on all twelve days, sleep duration on six. Duration comes first in the series,
        // so the name has to follow the count and not the order.
        var data = Enumerable.Range(0, 12)
            .Select(i => Entry(daysAgo: i, sleepHours: i < 6 ? 4 + i * 0.2 : null, emotion: Tier(i), quality: 1 + i % 5));

        var readiness = await Build(data).GetReadinessAsync(StatsRange.LastDays(30));

        readiness.PairedDays.Should().Be(12);
        readiness.BestFactorKey.Should().Be(CorrelationService.Factors.SleepQuality);
    }

    [Fact]
    public async Task Readiness_WithMoodAlone_NamesNothing()
    {
        var data = Enumerable.Range(0, 9).Select(i => Entry(daysAgo: i, emotion: Tier(i)));

        var readiness = await Build(data).GetReadinessAsync(StatsRange.LastDays(30));

        readiness.PairedDays.Should().Be(0);
        readiness.BestFactorKey.Should().BeNull();
    }

    // --- Energy axis (spec 18, phase 4) ---

    /// <summary>Energy rising with sleep, valence flat around zero: Relaxed (+1,-2) → Neutral → Playful (+1,+1) → Stressed (-1,+2).</summary>
    private static Emotion EnergyTier(int i) => i switch
    {
        < 5 => Emotion.Relaxed,   // arousal -2
        < 10 => Emotion.Neutral,  // 0
        < 15 => Emotion.Playful,  // +1
        _ => Emotion.Stressed     // +2
    };

    [Fact]
    public async Task EnergyAxis_SleepDurationVsEnergy_IsDetected_FromScalarDays()
    {
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 20; i++)
        {
            data.Add(Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: EnergyTier(i)));
        }
        var service = Build(data);

        var result = await service.GetEnergyCorrelationsAsync(StatsRange.LastDays(20));

        result.Should().ContainSingle();
        var sleep = result[0];
        sleep.Axis.Should().Be(MoodAxis.Energy);
        sleep.FactorKey.Should().Be(CorrelationService.Factors.SleepDuration);
        sleep.Coefficient.Should().BeGreaterThan(0.7);
        sleep.Holds.Should().BeTrue();
        sleep.SampleSize.Should().Be(20);
    }

    [Fact]
    public async Task EnergyAxis_ReadsHourlyObservations_WhenTheDayHasNoScalar()
    {
        // Each day logs two hours and no day-level emotion; energy is the mean arousal of those hours.
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 20; i++)
        {
            var dto = Entry(daysAgo: i, sleepHours: 4 + i * 0.2);
            dto.HourlyMood = new List<HourMood>
            {
                new() { Hour = 9, Mood = EnergyTier(i) },
                new() { Hour = 15, Mood = EnergyTier(i) }
            };
            data.Add(dto);
        }

        var result = await Build(data).GetEnergyCorrelationsAsync(StatsRange.LastDays(20));

        result.Should().ContainSingle();
        result[0].Axis.Should().Be(MoodAxis.Energy);
        result[0].Coefficient.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public async Task EnergyAxis_IsItsOwnFamily_TestsTheSameFactorsAndAnswersIndependently()
    {
        // Valence climbs with sleep (Sad → Anxious → Calm → Happy) while arousal flips -1, +1, -1, +1:
        // the mood pass finds sleep, the energy pass tests the same factor and rightly finds nothing.
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 20; i++)
        {
            data.Add(Entry(daysAgo: i, sleepHours: 4 + i * 0.2, emotion: Tier(i)));
        }
        var service = Build(data);

        var mood = await service.GetMoodCorrelationsAsync(StatsRange.LastDays(20));
        var energy = await service.GetEnergyCorrelationsAsync(StatsRange.LastDays(20));

        mood.Should().ContainSingle(c => c.FactorKey == CorrelationService.Factors.SleepDuration && c.Holds);
        energy.Should().ContainSingle(c => c.FactorKey == CorrelationService.Factors.SleepDuration);
        energy[0].Axis.Should().Be(MoodAxis.Energy);
        energy[0].Holds.Should().BeFalse();
        energy[0].SampleSize.Should().Be(mood[0].SampleSize);
        mood[0].Axis.Should().Be(MoodAxis.Valence);
    }

    [Fact]
    public async Task EnergyAxis_HonoursTheSameLag()
    {
        // Yesterday's sleep against today's energy: shift the energy tiers one day later.
        var data = new List<DiaryEntryStatsDto>();
        for (int i = 0; i < 21; i++)
        {
            var dto = Entry(daysAgo: i, sleepHours: 4 + i * 0.2);
            if (i > 0) dto.Emotion = EnergyTier(i - 1);
            data.Add(dto);
        }
        var service = Build(data);

        var sameDay = await service.GetEnergyCorrelationsAsync(StatsRange.LastDays(21));
        var lagged = await service.GetEnergyCorrelationsAsync(StatsRange.LastDays(21), lagDays: 1);

        lagged.Should().ContainSingle();
        lagged[0].LagDays.Should().Be(1);
        lagged[0].Coefficient.Should().BeGreaterThan(sameDay.Single().Coefficient);
    }
}
