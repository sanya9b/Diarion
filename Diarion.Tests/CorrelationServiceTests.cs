using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
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
            diary.Object, cycle.Object, profile.Object, todos.Object, finance.Object,
            new NullThemeClusterService());
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

        var result = await service.GetMoodCorrelationsAsync(days: 20);

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

        var result = await service.GetMoodCorrelationsAsync(days: 10);

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

        var result = await service.GetMoodCorrelationsAsync(days: 16);

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

        var lagged = await service.GetMoodCorrelationsAsync(days: 20, lagDays: 1);

        lagged.Should().ContainSingle();
        lagged[0].FactorKey.Should().Be("SleepDuration");
        lagged[0].LagDays.Should().Be(1);
        Math.Abs(lagged[0].Coefficient).Should().BeGreaterThan(0.7);
    }
}
