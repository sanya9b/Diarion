using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Correlations need fourteen paired days. Before this the card was simply absent until then, so the
/// statistics screen looked broken during exactly the fortnight in which people decide whether to keep
/// an app. These cover the progress shown in its place, and the related case of a tile naming a most
/// common mood when there is no single one.
/// </summary>
public class InsightReadinessTests
{
    private const int Days = 30;

    private static DateTime DateFor(int i) => DateTime.Today.AddDays(-i);

    private static CorrelationService BuildService(IEnumerable<DiaryEntryStatsDto> entries)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(entries.ToList());

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync(new List<CycleLog>());

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

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

    /// <summary>Days carrying both a mood and a sleep quality, which is one paired day each.</summary>
    private static List<DiaryEntryStatsDto> PairedDays(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new DiaryEntryStatsDto
            {
                Date = DateFor(i),
                Emotion = i % 2 == 0 ? Emotion.Happy : Emotion.Sad,
                SleepQuality = 1 + i % 5
            })
            .ToList();

    [Fact]
    public async Task Readiness_counts_the_days_that_actually_pair()
    {
        var readiness = await BuildService(PairedDays(6)).GetReadinessAsync(Days);

        readiness.PairedDays.Should().Be(6);
        readiness.RequiredDays.Should().Be(CorrelationService.MinSampleSize);
        readiness.IsReady.Should().BeFalse();
        readiness.DaysRemaining.Should().Be(CorrelationService.MinSampleSize - 6);
    }

    [Fact]
    public async Task A_day_with_a_factor_but_no_mood_is_not_progress()
    {
        // Sleep logged, mood never picked: nothing to correlate against, so it must not count.
        var entries = Enumerable.Range(0, 20)
            .Select(i => new DiaryEntryStatsDto { Date = DateFor(i), SleepQuality = 3 })
            .ToList();

        var readiness = await BuildService(entries).GetReadinessAsync(Days);

        readiness.PairedDays.Should().Be(0);
    }

    [Fact]
    public async Task Readiness_reports_the_best_factor_not_the_average()
    {
        // Sleep quality on twenty days, sleep duration on three. One factor clearing the bar is enough
        // for a first insight, so the better of the two is what the user is waiting on.
        var entries = PairedDays(20);
        for (var i = 0; i < 3; i++)
        {
            entries[i].SleepStart = new TimeSpan(23, 0, 0);
            entries[i].SleepEnd = new TimeSpan(7, 0, 0);
        }

        var readiness = await BuildService(entries).GetReadinessAsync(Days);

        readiness.PairedDays.Should().Be(20);
        readiness.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task Readiness_survives_a_diary_with_nothing_in_it()
    {
        var readiness = await BuildService(new List<DiaryEntryStatsDto>()).GetReadinessAsync(Days);

        readiness.PairedDays.Should().Be(0);
        readiness.IsReady.Should().BeFalse();
    }

    // ---- what the screen ends up showing -----------------------------------

    private static MoodStatsViewModel BuildViewModel(
        Dictionary<Emotion, int> emotionCounts,
        Emotion topEmotion,
        IReadOnlyList<MoodCorrelation>? correlations = null,
        CorrelationReadiness readiness = default)
    {
        var stats = new Mock<IStatisticsService>();
        stats.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<int>()))
             .ReturnsAsync(new MoodStatistics { EmotionCounts = emotionCounts, TopEmotion = topEmotion });

        var correlation = new Mock<ICorrelationService>();
        correlation.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                   .ReturnsAsync(correlations ?? new List<MoodCorrelation>());
        correlation.Setup(c => c.GetReadinessAsync(It.IsAny<int>(), It.IsAny<int>()))
                   .ReturnsAsync(readiness);

        return new MoodStatsViewModel(stats.Object, correlation.Object);
    }

    [Fact]
    public async Task With_no_insight_yet_the_screen_says_how_far_along_it_is()
    {
        var vm = BuildViewModel(
            new Dictionary<Emotion, int> { { Emotion.Happy, 6 }, { Emotion.Calm, 2 } },
            Emotion.Happy,
            readiness: new CorrelationReadiness(6, 14));

        await vm.LoadDataAsync(30);

        vm.HasCorrelations.Should().BeFalse();
        vm.HasInsightProgress.Should().BeTrue();
        vm.InsightProgressText.Should().Contain("6").And.Contain("14");
    }

    [Fact]
    public async Task Once_an_insight_exists_the_progress_line_goes_away()
    {
        var found = new List<MoodCorrelation>
        {
            new() { FactorKey = CorrelationService.Factors.SleepQuality, Coefficient = 0.6, Confidence = 4 }
        };

        var vm = BuildViewModel(
            new Dictionary<Emotion, int> { { Emotion.Happy, 20 } },
            Emotion.Happy,
            correlations: found);

        await vm.LoadDataAsync(30);

        vm.HasCorrelations.Should().BeTrue();
        vm.HasInsightProgress.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_diary_still_shows_progress_rather_than_nothing()
    {
        var vm = BuildViewModel(new Dictionary<Emotion, int>(), Emotion.None);

        await vm.LoadDataAsync(30);

        vm.IsEmpty.Should().BeTrue();
        vm.HasInsightProgress.Should().BeTrue();
    }

    [Fact]
    public async Task Three_emotions_tied_means_no_most_common_mood_is_claimed()
    {
        // The tile used to name whichever sorted first and the donut printed its share, both asserting
        // a winner the data does not have.
        var vm = BuildViewModel(
            new Dictionary<Emotion, int> { { Emotion.Calm, 1 }, { Emotion.Sad, 1 }, { Emotion.Angry, 1 } },
            Emotion.Calm);

        await vm.LoadDataAsync(30);

        vm.TopEmotionShareText.Should().BeEmpty("a share with no leader has nothing to attach to");
        vm.TopEmotionText.Should().Be(Diarion.Resources.Localization.AppResources.StatsNoLeadingEmotion);
    }

    [Fact]
    public async Task A_clear_winner_is_still_named_with_its_share()
    {
        var vm = BuildViewModel(
            new Dictionary<Emotion, int> { { Emotion.Happy, 6 }, { Emotion.Calm, 4 } },
            Emotion.Happy);

        await vm.LoadDataAsync(30);

        vm.TopEmotionShareText.Should().Contain("60");
        vm.TopEmotionText.Should().Be(Emotion.Happy.ToLocalizedName());
    }
}
