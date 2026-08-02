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

/// <summary>
/// The factors added beyond sleep and cycle. This is where holding eight domains in one app turns
/// into something a single-purpose competitor cannot do: habits, meals, tasks and spending are all
/// measured against the same mood series.
/// </summary>
public class CorrelationFactorsTests
{
    private const int Days = 24;

    private static Emotion Tier(int i) => i switch
    {
        < 6 => Emotion.Sad,
        < 12 => Emotion.Anxious,
        < 18 => Emotion.Calm,
        _ => Emotion.Happy
    };

    /// <summary>Day index 0 is the oldest, so a factor rising with the index rises with mood too.</summary>
    private static DateTime DateFor(int i) => DateTime.Today.AddDays(-(Days - 1 - i));

    private static CorrelationService Build(
        IEnumerable<DiaryEntryStatsDto> entries,
        IEnumerable<TodoStatsDto>? todos = null,
        IEnumerable<FinanceTransaction>? transactions = null)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(entries.ToList());

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync(new List<CycleLog>());

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

        var todoService = new Mock<ITodoService>();
        todoService.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                   .ReturnsAsync(todos?.ToList() ?? new List<TodoStatsDto>());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(transactions?.ToList() ?? new List<FinanceTransaction>());

        return new CorrelationService(
            diary.Object, cycle.Object, profile.Object, todoService.Object, finance.Object);
    }

    private static List<DiaryEntryStatsDto> MoodOnly() =>
        Enumerable.Range(0, Days)
            .Select(i => new DiaryEntryStatsDto { Date = DateFor(i), Emotion = Tier(i) })
            .ToList();

    private static MoodCorrelation? Find(IReadOnlyList<MoodCorrelation> all, string key)
        => all.FirstOrDefault(c => c.FactorKey == key);

    [Fact]
    public async Task Habit_completion_is_measured_against_mood()
    {
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            entries[i].HabitCompletion = i / (double)(Days - 1);
        }

        var result = await Build(entries).GetMoodCorrelationsAsync(Days);

        var habit = Find(result, CorrelationService.Factors.HabitCompletion);
        habit.Should().NotBeNull();
        habit!.Coefficient.Should().BeGreaterThan(0.8);
        habit.SampleSize.Should().Be(Days);
    }

    [Fact]
    public async Task Days_with_no_habits_configured_are_left_out_rather_than_scored_zero()
    {
        // A day without habits is not a day of total failure, and counting it as one would drag every
        // coefficient toward nothing.
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            entries[i].HabitCompletion = i < 4 ? null : 1.0 - (i / (double)Days);
        }

        var result = await Build(entries).GetMoodCorrelationsAsync(Days);

        Find(result, CorrelationService.Factors.HabitCompletion)!
            .SampleSize.Should().Be(Days - 4);
    }

    [Fact]
    public async Task Meals_logged_is_measured_but_a_blank_day_is_not_a_fast()
    {
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            entries[i].MealsLogged = i < 3 ? 0 : 1 + i % 5;
        }

        var result = await Build(entries).GetMoodCorrelationsAsync(Days);

        var meals = Find(result, CorrelationService.Factors.MealsLogged);
        meals.Should().NotBeNull();
        meals!.SampleSize.Should().Be(Days - 3, "zero means both 'ate nothing' and 'did not fill this in'");
    }

    [Fact]
    public async Task Task_completion_is_measured_as_a_share_of_the_day()
    {
        var todos = new List<TodoStatsDto>();
        for (var i = 0; i < Days; i++)
        {
            // Four tasks a day, more of them finished as mood improves.
            var done = i < Days / 2 ? 1 : 4;
            for (var t = 0; t < 4; t++)
            {
                todos.Add(new TodoStatsDto { TargetDate = DateFor(i), IsCompleted = t < done });
            }
        }

        var result = await Build(MoodOnly(), todos: todos).GetMoodCorrelationsAsync(Days);

        var tasks = Find(result, CorrelationService.Factors.TaskCompletion);
        tasks.Should().NotBeNull();
        tasks!.Coefficient.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task Days_with_nothing_planned_have_no_completion_rate()
    {
        var todos = Enumerable.Range(0, Days - 5)
            .Select(i => new TodoStatsDto { TargetDate = DateFor(i), IsCompleted = i % 2 == 0 })
            .ToList();

        var result = await Build(MoodOnly(), todos: todos).GetMoodCorrelationsAsync(Days);

        Find(result, CorrelationService.Factors.TaskCompletion)!
            .SampleSize.Should().Be(Days - 5, "a day with no tasks was not a day of failing at them");
    }

    [Fact]
    public async Task Spending_counts_a_day_with_no_transactions_as_a_real_zero()
    {
        // Unlike the other factors, spending nothing is genuine information rather than a gap.
        var transactions = new List<FinanceTransaction>();
        for (var i = 0; i < Days; i++)
        {
            if (i % 2 == 0)
            {
                transactions.Add(new FinanceTransaction
                {
                    Type = TransactionType.Expense,
                    Date = DateFor(i),
                    Amount = 10 + i
                });
            }
        }

        var result = await Build(MoodOnly(), transactions: transactions).GetMoodCorrelationsAsync(Days);

        var spend = Find(result, CorrelationService.Factors.DailySpend);
        spend.Should().NotBeNull();
        spend!.SampleSize.Should().Be(Days, "the quiet days are part of the series, not missing from it");
    }

    [Fact]
    public async Task Spending_is_not_backfilled_before_the_first_transaction_ever_recorded()
    {
        // Otherwise opening the finance module halfway through would invent a run of frugal days.
        var transactions = Enumerable.Range(Days / 2, Days / 2)
            .Select(i => new FinanceTransaction
            {
                Type = TransactionType.Expense,
                Date = DateFor(i),
                Amount = 5
            })
            .ToList();

        var result = await Build(MoodOnly(), transactions: transactions).GetMoodCorrelationsAsync(Days);

        // Half the window has no history at all, so the series is too short to report on.
        Find(result, CorrelationService.Factors.DailySpend).Should().BeNull();
    }

    [Fact]
    public async Task Income_is_not_counted_as_spending()
    {
        var transactions = Enumerable.Range(0, Days)
            .Select(i => new FinanceTransaction
            {
                Type = TransactionType.Income,
                Date = DateFor(i),
                Amount = 100
            })
            .ToList();

        var result = await Build(MoodOnly(), transactions: transactions).GetMoodCorrelationsAsync(Days);

        Find(result, CorrelationService.Factors.DailySpend).Should().BeNull();
    }

    [Fact]
    public async Task Every_reported_factor_carries_a_corrected_p_value()
    {
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            entries[i].HabitCompletion = i / (double)(Days - 1);
            entries[i].MealsLogged = 1 + i % 5;
            entries[i].SleepQuality = 1 + i % 5;
        }

        var result = await Build(entries).GetMoodCorrelationsAsync(Days);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c =>
        {
            c.AdjustedPValue.Should().BeGreaterThanOrEqualTo(c.PValue - 1e-12);
            c.Confidence.Should().Be(CorrelationStatistics.ConfidenceDots(c.AdjustedPValue));
        });
    }
}
