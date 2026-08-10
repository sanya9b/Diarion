using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.ViewModels;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The statistics screen used to speak in day counts, which pinned every window to today. These cover the
/// value that replaced them and the one thing a day count could never express: a window that already ended.
/// </summary>
public class StatisticsRangeTests
{
    // --- The value itself ---

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(365)]
    public void LastDays_IsNCalendarDaysEndingToday(int days)
    {
        var range = StatsRange.LastDays(days);

        range.End.Should().Be(DateTime.Today);
        range.Start.Should().Be(DateTime.Today.AddDays(-(days - 1)));
        range.Days.Should().Be(days, "both ends are inclusive");
    }

    [Fact]
    public void MonthToDate_RunsFromTheFirstOfTheMonthToToday()
    {
        var today = DateTime.Today;

        var range = StatsRange.MonthToDate();

        range.Start.Should().Be(new DateTime(today.Year, today.Month, 1));
        range.End.Should().Be(today);
        range.Days.Should().Be(today.Day);
    }

    [Fact]
    public void Normalized_SwapsAnInvertedPairAndDropsTheClock()
    {
        // A picker hands over midnight, but a range assembled in code may not.
        var inverted = new StatsRange(
            new DateTime(2026, 3, 31, 18, 45, 0),
            new DateTime(2026, 3, 1, 9, 15, 0));

        var range = inverted.Normalized();

        range.Start.Should().Be(new DateTime(2026, 3, 1));
        range.End.Should().Be(new DateTime(2026, 3, 31));
        range.Days.Should().Be(31);
    }

    // --- A window that already ended ---

    private static StatisticsService StatsOver(
        List<DiaryEntryStatsDto> entries,
        out Mock<IDiaryService> diary)
    {
        diary = new Mock<IDiaryService>();
        var captured = entries;
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync((DateTime start, DateTime end) =>
                 captured.Where(e => e.Date.Date >= start.Date && e.Date.Date <= end.Date).ToList());

        return new StatisticsService(
            diary.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);
    }

    /// <summary>March, read from an August that has nothing to do with it.</summary>
    private static StatsRange LastMarch => new(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

    [Fact]
    public async Task GetMoodStatisticsAsync_ForAWindowThatEnded_StopsAtTheWindowEnd()
    {
        // This is the bug the feature closes: the gap-fill used to run to DateTime.Today, so asking for
        // March returned March plus every empty day since — and the trend line trailed off into nothing.
        var service = StatsOver(
            new List<DiaryEntryStatsDto>
            {
                new() { Date = new DateTime(2026, 3, 5), Emotion = Emotion.Happy },
                new() { Date = new DateTime(2026, 3, 20), Emotion = Emotion.Sad }
            },
            out _);

        var result = await service.GetMoodStatisticsAsync(LastMarch);

        result.DailyTrend.Should().HaveCount(31);
        result.DailyTrend.First().Date.Should().Be(LastMarch.Start);
        result.DailyTrend.Last().Date.Should().Be(LastMarch.End);
        result.DailyTrend.Last().Date.Should().NotBe(DateTime.Today);
    }

    [Fact]
    public async Task GetSleepStatisticsAsync_ForAWindowThatEnded_StopsAtTheWindowEnd()
    {
        var service = StatsOver(
            new List<DiaryEntryStatsDto>
            {
                new()
                {
                    Date = new DateTime(2026, 3, 10),
                    SleepStart = new TimeSpan(23, 0, 0),
                    SleepEnd = new TimeSpan(7, 0, 0),
                    SleepQuality = 8
                }
            },
            out _);

        var result = await service.GetSleepStatisticsAsync(LastMarch);

        result.DailyData.Should().HaveCount(31);
        result.DailyData.Last().Date.Should().Be(LastMarch.End);
        result.AverageSleepDuration.TotalHours.Should().Be(8, "the one logged night is the only sample");
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_AsksTheDatabaseForExactlyTheWindow()
    {
        var service = StatsOver(new List<DiaryEntryStatsDto>(), out var diary);

        await service.GetMoodStatisticsAsync(LastMarch);

        diary.Verify(s => s.GetDiaryEntriesForStatsAsync(LastMarch.Start, LastMarch.End), Times.Once);
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_ForAWindowThatEnded_BaselineIsTheEqualWindowBefore()
    {
        // 31 days of March compare against 31 days ending 28 February, not against "the last 31 days".
        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FinanceTransaction>());
        finance.Setup(s => s.GetAccountsAsync(It.IsAny<bool>())).ReturnsAsync(new List<Account>());
        finance.Setup(s => s.GetTransfersAsync()).ReturnsAsync(new List<Transfer>());

        var service = new StatisticsService(
            new Mock<IDiaryService>().Object, new Mock<ITodoService>().Object, finance.Object);

        await service.GetFinanceStatisticsAsync(LastMarch);

        finance.Verify(s => s.GetFinanceTransactionsForStatsAsync(
            new DateTime(2026, 1, 29), LastMarch.End), Times.Once);
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_ForAWindowThatEnded_CountsTheWindowAndNotTheBaseline()
    {
        var rows = new List<FinanceTransaction>
        {
            new() { Type = TransactionType.Expense, Amount = 100m, Date = new DateTime(2026, 3, 15), Category = "Food" },
            new() { Type = TransactionType.Expense, Amount = 900m, Date = new DateTime(2026, 2, 15), Category = "Food" }
        };

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync((DateTime start, DateTime end) =>
                   rows.Where(r => r.Date >= start.Date && r.Date <= end.Date).ToList());
        finance.Setup(s => s.GetAccountsAsync(It.IsAny<bool>())).ReturnsAsync(new List<Account>());
        finance.Setup(s => s.GetTransfersAsync()).ReturnsAsync(new List<Transfer>());

        var service = new StatisticsService(
            new Mock<IDiaryService>().Object, new Mock<ITodoService>().Object, finance.Object);

        var stats = await service.GetFinanceStatisticsAsync(LastMarch);

        stats.TotalExpense.Should().Be(100m);
        stats.Comparison.Expense.Previous.Should().Be(900m);
    }

    // --- The screen ---

    private static StatisticsViewModel BuildViewModel(out Mock<IStatisticsService> stats)
        => BuildViewModel(out stats, out _);

    private static StatisticsViewModel BuildViewModel(
        out Mock<IStatisticsService> stats,
        out Mock<IDispatcherService> dispatcher)
    {
        stats = new Mock<IStatisticsService>();
        stats.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>()))
             .ReturnsAsync(new MoodStatistics());
        stats.Setup(s => s.GetSleepStatisticsAsync(It.IsAny<StatsRange>()))
             .ReturnsAsync(new SleepStatistics());
        stats.Setup(s => s.GetTodoStatisticsAsync(It.IsAny<StatsRange>()))
             .ReturnsAsync(new TodoStatistics());
        stats.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<StatsRange>(), It.IsAny<Guid?>()))
             .ReturnsAsync(new FinanceStatistics());

        var correlation = new Mock<ICorrelationService>();
        correlation.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
                   .ReturnsAsync(new List<MoodCorrelation>());
        correlation.Setup(c => c.GetReadinessAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
                   .ReturnsAsync(new CorrelationReadiness(0, CorrelationService.MinSampleSize));

        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetCurrentStreakAsync()).ReturnsAsync(new StreakResult(0, 0));

        var digest = new Mock<IDigestService>();
        digest.Setup(d => d.BuildAsync(
                  It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((DateTime start, DateTime end, int _, CancellationToken __) =>
                  new Digest(start, end, 0, 0, Array.Empty<DigestExcerpt>()));

        var themes = new Mock<IThemeClusterService>();
        themes.Setup(t => t.ClusterAsync(
                  It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<DiaryTheme>());

        var profile = new Mock<IProfileService>();
        profile.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetAccountsAsync(It.IsAny<bool>())).ReturnsAsync(new List<Account>());

        // Runs the action inline, standing in for a dispatcher that is already on the UI thread.
        dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(d => d.InvokeOnMainThread(It.IsAny<Action>()))
                  .Callback((Action action) => action());

        return new StatisticsViewModel(
            stats.Object,
            diary.Object,
            finance.Object,
            new Mock<INavigationService>().Object,
            dispatcher.Object,
            new MoodStatsViewModel(stats.Object, correlation.Object),
            digest.Object,
            themes.Object,
            new SleepStatsViewModel(stats.Object),
            new ProductivityStatsViewModel(stats.Object),
            new FinanceStatsViewModel(stats.Object, profile.Object),
            new HabitStatsViewModel(new Mock<IHabitService>().Object, profile.Object),
            new CycleStatsViewModel(new Mock<ICycleLogService>().Object, profile.Object));
    }

    [Fact]
    public void ViewModel_OpensOnTheCurrentMonthSoFar()
    {
        var vm = BuildViewModel(out _);

        vm.RangeStart.Should().Be(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        vm.RangeEnd.Should().Be(DateTime.Today);
        vm.MaxSelectableDate.Should().Be(DateTime.Today, "there is no data in the future");
    }

    [Fact]
    public async Task ViewModel_TappingAPreset_WritesItsWindowAndLightsOnlyThatChip()
    {
        var vm = BuildViewModel(out _);
        var month = vm.TimeRanges.Single(r => r.Option == TimeRangeOption.Month);

        await vm.SelectTimeRangeAsync(month);

        vm.RangeStart.Should().Be(DateTime.Today.AddDays(-29));
        vm.RangeEnd.Should().Be(DateTime.Today);
        vm.TimeRanges.Where(r => r.IsSelected).Should().ContainSingle().Which.Should().Be(month);
        vm.IsCustomRangeSelected.Should().BeFalse();
    }

    [Fact]
    public async Task ViewModel_EditingADate_DropsThePresetHighlight()
    {
        var vm = BuildViewModel(out _);
        await vm.SelectTimeRangeAsync(vm.TimeRanges.Single(r => r.Option == TimeRangeOption.Month));

        // One day shorter than the chip: the strip must stop claiming the window is still "1 month".
        vm.RangeStart = DateTime.Today.AddDays(-28);

        vm.TimeRanges.Should().OnlyContain(r => !r.IsSelected);
        vm.IsCustomRangeSelected.Should().BeTrue();
    }

    [Fact]
    public void ViewModel_MovingTheStartPastTheEnd_PushesTheEndAlong()
    {
        var vm = BuildViewModel(out _);
        vm.RangeStart = DateTime.Today.AddDays(-10);
        vm.RangeEnd = DateTime.Today.AddDays(-5);

        vm.RangeStart = DateTime.Today.AddDays(-2);

        vm.RangeEnd.Should().Be(DateTime.Today.AddDays(-2), "an inverted window reads as 'you logged nothing'");
        vm.CurrentRange.Days.Should().Be(1);
    }

    [Fact]
    public void ViewModel_MovingTheEndBeforeTheStart_PullsTheStartBack()
    {
        var vm = BuildViewModel(out _);
        vm.RangeStart = DateTime.Today.AddDays(-10);

        vm.RangeEnd = DateTime.Today.AddDays(-20);

        vm.RangeStart.Should().Be(DateTime.Today.AddDays(-20));
        vm.CurrentRange.Start.Should().BeOnOrBefore(vm.CurrentRange.End);
    }

    [Fact]
    public async Task ViewModel_ADebouncedReload_GoesBackToTheUiThread()
    {
        // Editing a date reloads off a timer, i.e. on the thread pool. Every tab's load replaces bound
        // collections, and WinUI ignores those writes from a background thread without complaining:
        // the charts stayed on the previous window while the numbers beside them moved on.
        var vm = BuildViewModel(out var stats, out var dispatcher);
        stats.Invocations.Clear();

        vm.RangeStart = LastMarch.Start;
        vm.RangeEnd = LastMarch.End;

        await vm.FlushPendingReloadAsync();

        dispatcher.Verify(d => d.InvokeOnMainThread(It.IsAny<Action>()), Times.AtLeastOnce);
        stats.Verify(s => s.GetMoodStatisticsAsync(LastMarch), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ViewModel_LoadsExactlyTheChosenWindow()
    {
        var vm = BuildViewModel(out var stats);
        vm.RangeStart = LastMarch.Start;
        vm.RangeEnd = LastMarch.End;
        stats.Invocations.Clear();

        await vm.LoadStatisticsAsync();

        stats.Verify(s => s.GetMoodStatisticsAsync(LastMarch), Times.Once);
    }

    [Fact]
    public async Task ViewModel_SleepTab_GetsTheSameWindowAsTheRest()
    {
        var vm = BuildViewModel(out var stats);
        vm.SelectTab(vm.Tabs.Single(t => t.Option == StatisticsTabOption.Sleep));
        vm.RangeStart = LastMarch.Start;
        vm.RangeEnd = LastMarch.End;
        stats.Invocations.Clear();

        await vm.LoadStatisticsAsync();

        stats.Verify(s => s.GetSleepStatisticsAsync(LastMarch), Times.Once);
    }
}
