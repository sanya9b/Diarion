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

public class StatisticsServiceTests
{
    [Fact]
    public async Task GetSleepStatisticsAsync_ShouldCalculateCorrectly()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            new DiaryEntryStatsDto { Date = today.AddDays(-2), SleepStart = new TimeSpan(23, 0, 0), SleepEnd = new TimeSpan(7, 0, 0), SleepQuality = 8 },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), SleepStart = new TimeSpan(0, 0, 0), SleepEnd = new TimeSpan(6, 0, 0), SleepQuality = 6 }, // 6 hours
            new DiaryEntryStatsDto { Date = today, SleepStart = null, SleepEnd = null, SleepQuality = 0 } // No sleep data
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);
        var mockTodoService = new Mock<ITodoService>();

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object, new Mock<IFinanceService>().Object);

        // Act
        var result = await statsService.GetSleepStatisticsAsync(7);

        // Assert
        result.AverageSleepQuality.Should().Be(7.0); // (8+6)/2
        result.AverageSleepDuration.TotalHours.Should().Be(7.0); // (8h + 6h) / 2
        
        // A "7 day" window is exactly 7 calendar days including today.
        result.DailyData.Should().HaveCount(7);
        result.DailyData.FirstOrDefault(d => d.Date == today.AddDays(-2))?.Duration.TotalHours.Should().Be(8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public async Task GetSleepStatisticsAsync_ReturnsExactlyNDailyPoints(int days)
    {
        var mockDiaryService = new Mock<IDiaryService>();
        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<DiaryEntryStatsDto>());

        var statsService = new StatisticsService(
            mockDiaryService.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);

        var result = await statsService.GetSleepStatisticsAsync(days);

        result.DailyData.Should().HaveCount(days);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_ShouldCalculateTopEmotion()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            new DiaryEntryStatsDto { Date = today.AddDays(-2), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today, Emotion = Emotion.Sad }
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);
        var mockTodoService = new Mock<ITodoService>();

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object, new Mock<IFinanceService>().Object);

        // Act
        var result = await statsService.GetMoodStatisticsAsync(7);

        // Assert
        result.TopEmotion.Should().Be(Emotion.Happy);
        result.EmotionCounts[Emotion.Happy].Should().Be(2);
        result.EmotionCounts[Emotion.Sad].Should().Be(1);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_BuildsGapFilledDailyTrend()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            // Two emotions the same day -> averaged valence (2 + 1) / 2 = 1.5
            new DiaryEntryStatsDto { Date = today.AddDays(-3), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today.AddDays(-3), Emotion = Emotion.Calm },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Sad },
            new DiaryEntryStatsDto { Date = today, Emotion = Emotion.None } // no mood logged today
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);

        var statsService = new StatisticsService(
            mockDiaryService.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);

        // Act
        var result = await statsService.GetMoodStatisticsAsync(7);

        // Assert
        result.DailyTrend.Should().HaveCount(7); // exactly N days, gap-filled
        result.DailyTrend.Count(p => p.HasData).Should().Be(2);

        result.DailyTrend.Single(p => p.Date == today.AddDays(-3)).Valence.Should().Be(1.5);
        result.DailyTrend.Single(p => p.Date == today.AddDays(-1)).Valence.Should().Be(-2);
        result.DailyTrend.Single(p => p.Date == today).HasData.Should().BeFalse();
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_DailyTrend_SetsDominantEmotionPerDay()
    {
        // Arrange: one day with Happy x2 + Sad x1 -> dominant is Happy (mode).
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Sad },
            new DiaryEntryStatsDto { Date = today, Emotion = Emotion.Calm }
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);

        var statsService = new StatisticsService(
            mockDiaryService.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);

        // Act
        var result = await statsService.GetMoodStatisticsAsync(7);

        // Assert
        result.DailyTrend.Single(p => p.Date == today.AddDays(-1)).DominantEmotion.Should().Be(Emotion.Happy);
        result.DailyTrend.Single(p => p.Date == today).DominantEmotion.Should().Be(Emotion.Calm);
    }

    [Fact]
    public async Task GetTodoStatisticsAsync_ShouldCalculateCorrectly()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<TodoStatsDto>
        {
            new TodoStatsDto { TargetDate = today.AddDays(-1), IsCompleted = true },
            new TodoStatsDto { TargetDate = today.AddDays(-1), IsCompleted = false },
            new TodoStatsDto { TargetDate = today, IsCompleted = true }
        };

        var mockTodoService = new Mock<ITodoService>();
        mockTodoService.Setup(s => s.GetTodoStatsSummaryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new TodoStatistics { TotalCount = 3, CompletedCount = 2 });

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object, new Mock<IFinanceService>().Object);

        // Act
        var result = await statsService.GetTodoStatisticsAsync(7);

        // Assert
        result.TotalCount.Should().Be(3);
        result.CompletedCount.Should().Be(2);
        result.CompletionPercentage.Should().BeApproximately(0.666, 0.01);
    }

    // --- Hourly mood ---

    private static StatisticsService MoodStatsServiceOver(List<DiaryEntryStatsDto> entries)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(entries);

        return new StatisticsService(diary.Object, new Mock<ITodoService>().Object, new Mock<IFinanceService>().Object);
    }

    private static List<HourMood> Hours(params (int Hour, Emotion Mood)[] entries) =>
        entries.Select(e => new HourMood { Hour = e.Hour, Mood = e.Mood }).ToList();

    [Fact]
    public async Task GetMoodStatisticsAsync_ValenceAveragesTheLoggedHours()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new()
            {
                Date = today,
                Emotion = Emotion.Happy,                                  // +2, must be overridden
                HourlyMood = Hours((9, Emotion.Sad), (18, Emotion.Calm))  // -2 and +1 → -0.5
            }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.DailyTrend.Single(p => p.Date == today).Valence.Should().Be(-0.5);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_CountsOneObservationPerDay_NotPerHour()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            // A heavily logged day must not outweigh a plain one in the donut.
            new() { Date = today.AddDays(-1), HourlyMood = Hours((8, Emotion.Sad), (9, Emotion.Sad), (10, Emotion.Sad)) },
            new() { Date = today, Emotion = Emotion.Happy }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.EmotionCounts[Emotion.Sad].Should().Be(1);
        result.EmotionCounts[Emotion.Happy].Should().Be(1);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_ScalarOnlyDays_BehaveExactlyAsBefore()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today.AddDays(-1), Emotion = Emotion.Calm },
            new() { Date = today, Emotion = Emotion.Calm }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.EmotionCounts[Emotion.Calm].Should().Be(2);
        result.TopEmotion.Should().Be(Emotion.Calm);
        result.DailyTrend.Single(p => p.Date == today).Valence.Should().Be(Emotion.Calm.ToValence());
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_DayWithNoMoodAtAll_IsNotCounted()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today, Emotion = Emotion.None }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.DailyTrend.Single(p => p.Date == today).HasData.Should().BeFalse();
    }

    // --- Hour-of-day profile ---

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_HasSeventeenSlotsFrom7To23()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.Calm)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.HourlyProfile.Should().HaveCount(17);
        result.HourlyProfile.Select(p => p.Hour).Should().BeInAscendingOrder();
        result.HourlyProfile.First().Hour.Should().Be(7);
        result.HourlyProfile.Last().Hour.Should().Be(23);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_ScalarOnlyDay_ContributesNothing()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = DateTime.Today, Emotion = Emotion.Happy }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.HourlyProfile.Should().OnlyContain(p => !p.HasData && p.Count == 0);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_AveragesMixedEmotionsAtSameHour()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today.AddDays(-1), HourlyMood = Hours((9, Emotion.Happy)) }, // +2
            new() { Date = today, HourlyMood = Hours((9, Emotion.Sad)) }                // -2
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour9 = result.HourlyProfile.Single(p => p.Hour == 9);
        hour9.Valence.Should().Be(0);
        hour9.Count.Should().Be(2);
        hour9.HasData.Should().BeTrue();
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_CountsObservationsPerHour()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today.AddDays(-2), HourlyMood = Hours((21, Emotion.Angry)) },
            new() { Date = today.AddDays(-1), HourlyMood = Hours((21, Emotion.Angry)) },
            new() { Date = today, HourlyMood = Hours((21, Emotion.Angry)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour21 = result.HourlyProfile.Single(p => p.Hour == 21);
        hour21.Count.Should().Be(3);
        hour21.Valence.Should().Be(-2);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_UnloggedHour_IsFlaggedWithoutData()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.Calm)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour14 = result.HourlyProfile.Single(p => p.Hour == 14);
        hour14.HasData.Should().BeFalse();
        hour14.Count.Should().Be(0);
        hour14.Valence.Should().Be(0);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_IgnoresNoneAndOutOfRangeHours()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            // Hour 3 predates the 7..23 scale and can only come from imported or legacy data.
            new() { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.None), (3, Emotion.Happy), (9, Emotion.Happy)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.HourlyProfile.Single(p => p.Hour == 9).Count.Should().Be(1);
        result.HourlyProfile.Should().NotContain(p => p.Hour == 3);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_IsWeightedByObservation_NotByDay()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today.AddDays(-1), HourlyMood = Hours((9, Emotion.Happy), (9, Emotion.Happy)) }, // +2, +2
            new() { Date = today, HourlyMood = Hours((9, Emotion.Sad)) }                                    // -2
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour9 = result.HourlyProfile.Single(p => p.Hour == 9);
        hour9.Count.Should().Be(3);
        hour9.Valence.Should().BeApproximately(0.667, 0.001);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_DayCountCountsDistinctDates()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today.AddDays(-1), HourlyMood = Hours((9, Emotion.Calm), (9, Emotion.Happy)) },
            new() { Date = today, HourlyMood = Hours((9, Emotion.Calm)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour9 = result.HourlyProfile.Single(p => p.Hour == 9);
        hour9.Count.Should().Be(3);
        hour9.DayCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_DayCountCollapsesRowsSharingADate()
    {
        var today = DateTime.Today;
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = today, HourlyMood = Hours((9, Emotion.Calm)) },
            new() { Date = today, HourlyMood = Hours((9, Emotion.Sad)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        var hour9 = result.HourlyProfile.Single(p => p.Hour == 9);
        hour9.Count.Should().Be(2);
        hour9.DayCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_EmptyHour_HasZeroDayCount()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.Calm)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.HourlyProfile.Single(p => p.Hour == 14).DayCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_HourlyProfile_DayCountIsPerHour()
    {
        var service = MoodStatsServiceOver(new List<DiaryEntryStatsDto>
        {
            new() { Date = DateTime.Today, HourlyMood = Hours((9, Emotion.Calm), (10, Emotion.Happy)) }
        });

        var result = await service.GetMoodStatisticsAsync(7);

        result.HourlyProfile.Single(p => p.Hour == 9).DayCount.Should().Be(1);
        result.HourlyProfile.Single(p => p.Hour == 10).DayCount.Should().Be(1);
    }

    // --- Finance reports ---

    private static (StatisticsService Service, Mock<IFinanceService> Finance) FinanceServiceOver(
        List<FinanceTransaction> rows,
        List<Account>? accounts = null,
        List<Transfer>? transfers = null)
    {
        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync((DateTime start, DateTime end) =>
                   rows.Where(r => r.Date >= start.Date && r.Date <= end.Date).ToList());
        finance.Setup(s => s.GetAccountsAsync(It.IsAny<bool>())).ReturnsAsync(accounts ?? new List<Account>());
        finance.Setup(s => s.GetTransfersAsync()).ReturnsAsync(transfers ?? new List<Transfer>());

        var service = new StatisticsService(
            new Mock<IDiaryService>().Object, new Mock<ITodoService>().Object, finance.Object);
        return (service, finance);
    }

    private static FinanceTransaction Spend(decimal amount, DateTime date, Guid? accountId = null)
        => new() { Type = TransactionType.Expense, Amount = amount, Date = date, Category = "Food", AccountId = accountId };

    [Fact]
    public async Task GetFinanceStatisticsAsync_FetchesTwiceTheSelectedWindow()
    {
        // Pins the widened fetch. Narrowed back to N days the comparison card would silently show every
        // figure as "new", which looks like a plausible empty state rather than a bug.
        var (service, finance) = FinanceServiceOver(new List<FinanceTransaction>());

        await service.GetFinanceStatisticsAsync(30);

        finance.Verify(s => s.GetFinanceTransactionsForStatsAsync(
            DateTime.Today.AddDays(-59), DateTime.Today), Times.Once);
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_KpisCoverOnlyTheSelectedWindow_NotTheBaseline()
    {
        var rows = new List<FinanceTransaction>
        {
            Spend(100m, DateTime.Today),
            Spend(900m, DateTime.Today.AddDays(-40))   // inside the fetch, outside the window
        };
        var (service, _) = FinanceServiceOver(rows);

        var stats = await service.GetFinanceStatisticsAsync(30);

        stats.TotalExpense.Should().Be(100m);
        stats.Comparison.Expense.Previous.Should().Be(900m, "the baseline is what the wider fetch is for");
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_TrendTotalsEqualTheKpiTotals()
    {
        // The chart sits directly under the KPI tiles; one fetch is what makes them structurally agree.
        var rows = Enumerable.Range(0, 20)
            .Select(offset => Spend(10m, DateTime.Today.AddDays(-offset)))
            .ToList();
        var (service, _) = FinanceServiceOver(rows);

        var stats = await service.GetFinanceStatisticsAsync(30);

        stats.Trend.TotalExpense.Should().Be(stats.TotalExpense);
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_WithAnAccount_ScopesTotalsAndSkipsTheBreakdown()
    {
        var card = new Account { Name = "Card" };
        var cash = new Account { Name = "Cash" };
        var rows = new List<FinanceTransaction>
        {
            Spend(100m, DateTime.Today, card.Id),
            Spend(250m, DateTime.Today, cash.Id)
        };
        var (service, _) = FinanceServiceOver(rows, new List<Account> { card, cash });

        var scoped = await service.GetFinanceStatisticsAsync(30, card.Id);

        scoped.TotalExpense.Should().Be(100m);
        scoped.Trend.TotalExpense.Should().Be(100m);
        scoped.AccountBreakdown.Should().BeEmpty("one account is not a breakdown");
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_WithAnAccount_RequestsTheSameDbRangeAsWithout()
    {
        // Guards against pushing the account predicate into LiteDB later: AccountId is a nullable Guid,
        // whose LINQ translation is broken there, and it fails by returning nothing at all.
        var card = new Account { Name = "Card" };
        var (service, finance) = FinanceServiceOver(new List<FinanceTransaction>(), new List<Account> { card });

        await service.GetFinanceStatisticsAsync(30);
        await service.GetFinanceStatisticsAsync(30, card.Id);

        finance.Verify(s => s.GetFinanceTransactionsForStatsAsync(
            DateTime.Today.AddDays(-59), DateTime.Today), Times.Exactly(2));
    }

    [Fact]
    public async Task GetFinanceStatisticsAsync_AccountBreakdownSumsToTheHeadlineTotals()
    {
        var card = new Account { Name = "Card" };
        var rows = new List<FinanceTransaction>
        {
            Spend(100m, DateTime.Today, card.Id),
            Spend(40m, DateTime.Today, null)      // never assigned — must still be counted somewhere
        };
        var (service, _) = FinanceServiceOver(rows, new List<Account> { card });

        var stats = await service.GetFinanceStatisticsAsync(30);

        stats.AccountBreakdown.Sum(r => r.Expense).Should().Be(stats.TotalExpense);
    }
}