using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Services;
using Diarion.Services.Ai.Reports;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class SnapshotBuilderTests
{
    private static readonly StatsRange Week = new(new DateTime(2026, 8, 3), new DateTime(2026, 8, 9));

    private readonly Mock<IStatisticsService> _statistics = new(MockBehavior.Strict);
    private readonly Mock<ICorrelationService> _correlations = new(MockBehavior.Strict);
    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<IHabitService> _habits = new();
    private readonly Mock<ICycleLogService> _cycle = new();
    private readonly Mock<IProfileService> _profile = new();
    private readonly Mock<IGuidedPromptService> _prompts = new();

    private readonly List<DiaryEntry> _entries = new();
    private readonly List<CycleLog> _cycleLogs = new();

    private SleepStatistics _sleep = new();
    private MoodStatistics _mood = new();
    private TodoStatistics _todos = new();
    private FinanceStatistics _finance = new();
    private List<MoodCorrelation> _sameDay = new();
    private List<MoodCorrelation> _lagged = new();
    private List<HabitCompletionHistory> _habitHistory = new();
    private List<HarmfulHabitTracker> _trackers = new();

    public SnapshotBuilderTests()
    {
        _profile.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { CurrencyCode = "UAH" });

        _statistics.Setup(s => s.GetSleepStatisticsAsync(It.IsAny<StatsRange>())).ReturnsAsync(() => _sleep);
        _statistics.Setup(s => s.GetMoodStatisticsAsync(It.IsAny<StatsRange>())).ReturnsAsync(() => _mood);
        _statistics.Setup(s => s.GetTodoStatisticsAsync(It.IsAny<StatsRange>())).ReturnsAsync(() => _todos);
        _statistics.Setup(s => s.GetFinanceStatisticsAsync(It.IsAny<StatsRange>(), It.IsAny<Guid?>()))
            .ReturnsAsync(() => _finance);

        _correlations.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), 0))
            .ReturnsAsync(() => _sameDay);
        _correlations.Setup(c => c.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), 1))
            .ReturnsAsync(() => _lagged);

        _diary.Setup(d => d.GetAllEntriesAsync()).ReturnsAsync(() => _entries);
        _habits.Setup(h => h.GetHabitCompletionsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(() => _habitHistory);
        _habits.Setup(h => h.GetHarmfulHabitTrackersAsync()).ReturnsAsync(() => _trackers);
        _cycle.Setup(c => c.GetLogsAsync()).ReturnsAsync(() => _cycleLogs);
        _prompts.Setup(p => p.GetLibraryAsync()).ReturnsAsync(PromptLibrary.Empty);
    }

    private SnapshotBuilder Builder() => new(
        _statistics.Object,
        _correlations.Object,
        _diary.Object,
        _habits.Object,
        _cycle.Object,
        _profile.Object,
        _prompts.Object);

    private Task<PeriodSnapshot> BuildAsync(SnapshotOptions? options = null)
        => Builder().BuildAsync(PeriodKind.Week, Week, options ?? SnapshotOptions.Default);

    private void Entry(DateTime date, Action<DiaryEntry> configure)
    {
        var entry = new DiaryEntry { Date = date };
        configure(entry);
        _entries.Add(entry);
    }

    [Fact]
    public async Task Header_names_the_period_the_caller_asked_for()
    {
        var snapshot = await BuildAsync();

        snapshot.PeriodKind.Should().Be("week");
        snapshot.Start.Should().Be("2026-08-03");
        snapshot.End.Should().Be("2026-08-09");
        snapshot.DayCount.Should().Be(7);
        snapshot.Currency.Should().Be("UAH");
        snapshot.Language.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Every_calendar_day_gets_a_row_even_when_nothing_was_written()
    {
        Entry(new DateTime(2026, 8, 5), e => e.Content = "середа");

        var snapshot = await BuildAsync();

        snapshot.Days.Should().HaveCount(7);
        snapshot.Days.Select(d => d.Date).Should().BeInAscendingOrder();
        snapshot.Days.Single(d => d.Date == "2026-08-05").Text.Should().Be("середа");

        // The other six are present and visibly empty — the difference between a quiet week and a
        // week the app simply did not look at.
        snapshot.Days.Count(d => d.Text is null).Should().Be(6);
    }

    [Fact]
    public async Task Days_outside_the_window_do_not_leak_in()
    {
        Entry(new DateTime(2026, 8, 2), e => e.Content = "неділя перед");
        Entry(new DateTime(2026, 8, 10), e => e.Content = "понеділок після");

        var snapshot = await BuildAsync();

        snapshot.Days.Should().OnlyContain(d => d.Text == null);
    }

    [Fact]
    public async Task Blank_text_becomes_null_rather_than_an_empty_string()
    {
        Entry(new DateTime(2026, 8, 4), e =>
        {
            e.Content = "   ";
            e.Gratitude = "  за дощ  ";
        });

        var day = (await BuildAsync()).Days.Single(d => d.Date == "2026-08-04");

        day.Text.Should().BeNull();
        day.Gratitude.Should().Be("за дощ");
    }

    [Fact]
    public async Task Intimate_life_stays_home_unless_it_is_switched_on()
    {
        Entry(new DateTime(2026, 8, 4), e => e.IntimateLife = "приватне");

        var withoutIt = (await BuildAsync()).Days.Single(d => d.Date == "2026-08-04");
        withoutIt.IntimateLife.Should().BeNull();

        var withIt = (await BuildAsync(new SnapshotOptions { IncludeIntimateLife = true }))
            .Days.Single(d => d.Date == "2026-08-04");
        withIt.IntimateLife.Should().Be("приватне");
    }

    [Fact]
    public async Task Cycle_stays_home_unless_it_is_switched_on()
    {
        _cycleLogs.Add(new CycleLog { Date = new DateTime(2026, 8, 5), Symptoms = { "CycleSymptomCramps" } });

        (await BuildAsync()).Cycle.Should().BeNull();
        _cycle.Verify(c => c.GetLogsAsync(), Times.Never);

        var included = await BuildAsync(new SnapshotOptions { IncludeCycle = true });
        included.Cycle.Should().ContainSingle()
            .Which.Symptoms.Should().ContainSingle().Which.Should().Be("CycleSymptomCramps");
    }

    [Fact]
    public async Task Cycle_days_outside_the_window_are_dropped()
    {
        _cycleLogs.Add(new CycleLog { Date = new DateTime(2026, 7, 30) });
        _cycleLogs.Add(new CycleLog { Date = new DateTime(2026, 8, 5), IsSymptomOnly = true });

        var snapshot = await BuildAsync(new SnapshotOptions { IncludeCycle = true });

        snapshot.Cycle.Should().ContainSingle();
        snapshot.Cycle![0].Date.Should().Be("2026-08-05");
        snapshot.Cycle[0].IsPeriodDay.Should().BeFalse();
    }

    [Fact]
    public async Task A_night_nobody_logged_is_null_and_not_zero_hours()
    {
        _sleep = new SleepStatistics
        {
            AverageSleepDuration = TimeSpan.FromHours(7.3333),
            AverageSleepQuality = 4.006,
            DailyData =
            {
                new SleepDataPoint { Date = new DateTime(2026, 8, 3), Duration = TimeSpan.FromHours(7.5), Quality = 4 },
                new SleepDataPoint { Date = new DateTime(2026, 8, 4), Duration = TimeSpan.Zero, Quality = 0 }
            }
        };

        var snapshot = await BuildAsync();

        snapshot.Sleep.AverageHours.Should().Be(7.33);
        snapshot.Sleep.AverageQuality.Should().Be(4.01);
        snapshot.Sleep.Daily[0].Hours.Should().Be(7.5);
        snapshot.Sleep.Daily[1].Hours.Should().BeNull();
        snapshot.Sleep.Daily[1].Quality.Should().BeNull();
    }

    [Fact]
    public async Task Mood_keeps_the_gaps_and_drops_the_hours_nobody_logged()
    {
        _mood = new MoodStatistics
        {
            TopEmotion = Emotion.Calm,
            EmotionCounts = { [Emotion.Calm] = 3, [Emotion.Sad] = 3, [Emotion.Happy] = 5, [Emotion.None] = 9 },
            DailyTrend =
            {
                new MoodTrendPoint { Date = new DateTime(2026, 8, 3), Valence = 1.239, HasData = true, DominantEmotion = Emotion.Happy },
                new MoodTrendPoint { Date = new DateTime(2026, 8, 4), Valence = 0, HasData = false }
            },
            HourlyProfile =
            {
                new MoodHourPoint { Hour = 9, Valence = 1.5, Count = 4, DayCount = 3, HasData = true },
                new MoodHourPoint { Hour = 10, Valence = 0, Count = 0, DayCount = 0, HasData = false }
            }
        };

        var snapshot = await BuildAsync();

        snapshot.Mood.TopEmotion.Should().Be("Calm");
        snapshot.Mood.Daily[0].Valence.Should().Be(1.24);
        snapshot.Mood.Daily[1].Valence.Should().BeNull();
        snapshot.Mood.Daily[1].DominantEmotion.Should().BeNull();

        snapshot.Mood.ByHour.Should().ContainSingle();
        snapshot.Mood.ByHour[0].Hour.Should().Be(9);
        snapshot.Mood.ByHour[0].Observations.Should().Be(4);
        snapshot.Mood.ByHour[0].Days.Should().Be(3);

        // Commonest first; the tie broken by name so two runs cannot disagree. None is not an emotion.
        snapshot.Mood.Emotions.Select(e => e.Emotion).Should().Equal("Happy", "Calm", "Sad");
    }

    [Fact]
    public async Task Finance_reports_the_earlier_window_only_when_there_was_one()
    {
        _finance = new FinanceStatistics
        {
            TotalIncome = 1000m,
            TotalExpense = 250.005m,
            ExpenseByCategory =
            {
                new CategoryStatItem { Category = "Їжа", Amount = 100m },
                new CategoryStatItem { Category = "Транспорт", Amount = 150m }
            }
        };

        var withoutBaseline = await BuildAsync();
        withoutBaseline.Finance.VersusPrevious.Should().BeNull();
        withoutBaseline.Finance.Expense.Should().Be(250.01m);

        // Largest category first, whatever order the statistics layer produced.
        withoutBaseline.Finance.ExpenseByCategory.Select(c => c.Label).Should().Equal("Транспорт", "Їжа");

        _finance.Comparison = new FinanceComparisonReport
        {
            Income = new FinanceMetricDelta { Current = 1000m, Previous = 800m },
            Expense = new FinanceMetricDelta { Current = 250m, Previous = 300m }
        };

        var withBaseline = await BuildAsync();
        withBaseline.Finance.VersusPrevious!.PreviousIncome.Should().Be(800m);
        withBaseline.Finance.VersusPrevious.PreviousExpense.Should().Be(300m);
    }

    [Fact]
    public async Task Correlations_from_both_lags_arrive_strongest_first()
    {
        _sameDay = new List<MoodCorrelation>
        {
            new() { FactorKey = "FactorSleepDuration", Coefficient = 0.4123, AdjustedPValue = 0.0123456, SampleSize = 7 }
        };
        _lagged = new List<MoodCorrelation>
        {
            new() { FactorKey = "FactorHabitCompletion", Coefficient = -0.7891, AdjustedPValue = 0.04, SampleSize = 6, LagDays = 1 }
        };

        var snapshot = await BuildAsync();

        snapshot.Correlations.Select(c => c.Factor)
            .Should().Equal("FactorHabitCompletion", "FactorSleepDuration");
        snapshot.Correlations[0].Coefficient.Should().Be(-0.789);
        snapshot.Correlations[0].LagDays.Should().Be(1);
        snapshot.Correlations[1].AdjustedPValue.Should().Be(0.0123);
    }

    [Fact]
    public async Task A_habit_is_counted_against_the_days_it_was_due_not_the_whole_week()
    {
        var monWedFri = new RecurrenceRule
        {
            Kind = RecurrenceKind.Weekly,
            DaysOfWeek = { (int)DayOfWeek.Monday, (int)DayOfWeek.Wednesday, (int)DayOfWeek.Friday }
        };

        _habitHistory = new List<HabitCompletionHistory>
        {
            new()
            {
                Name = "Зарядка",
                CreatedAt = new DateTime(2026, 1, 1),
                Schedule = monWedFri,
                CompletedDates =
                {
                    new DateTime(2026, 8, 3), new DateTime(2026, 8, 5), new DateTime(2026, 8, 7),
                    new DateTime(2026, 7, 31)
                }
            }
        };

        var habit = (await BuildAsync()).Habits.Good.Should().ContainSingle().Subject;

        habit.ScheduledDays.Should().Be(3);
        habit.CompletedDays.Should().Be(3, "the fourth tick is in the week before this one");
    }

    [Fact]
    public async Task A_habit_created_midweek_is_not_due_before_it_existed()
    {
        _habitHistory = new List<HabitCompletionHistory>
        {
            new()
            {
                Name = "Читання",
                CreatedAt = new DateTime(2026, 8, 7),
                Schedule = new RecurrenceRule { Kind = RecurrenceKind.Daily },
                CompletedDates = { new DateTime(2026, 8, 8) }
            }
        };

        (await BuildAsync()).Habits.Good[0].ScheduledDays.Should().Be(3);
    }

    [Fact]
    public async Task Quit_trackers_count_this_period_and_not_the_whole_history()
    {
        _trackers = new List<HarmfulHabitTracker>
        {
            new()
            {
                HarmfulHabitName = "Цигарки",
                MarkedDays = { new DateTime(2026, 7, 1), new DateTime(2026, 8, 4), new DateTime(2026, 8, 5) },
                Relapses =
                {
                    new RelapseEvent { Date = new DateTime(2026, 8, 6) },
                    new RelapseEvent { Date = new DateTime(2025, 12, 1) }
                }
            }
        };

        var tracker = (await BuildAsync()).Habits.Quitting.Should().ContainSingle().Subject;

        tracker.MarkedDays.Should().Be(2);
        tracker.Relapses.Should().Be(1);
    }

    [Fact]
    public async Task An_upside_down_range_is_read_the_right_way_round()
    {
        var snapshot = await Builder().BuildAsync(
            PeriodKind.Week,
            new StatsRange(Week.End, Week.Start),
            SnapshotOptions.Default);

        snapshot.Start.Should().Be("2026-08-03");
        snapshot.End.Should().Be("2026-08-09");
        snapshot.Days.Should().HaveCount(7);
    }

    [Fact]
    public async Task Cancellation_stops_the_build()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Builder().BuildAsync(PeriodKind.Week, Week, SnapshotOptions.Default, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Building_the_same_week_twice_produces_the_same_bytes()
    {
        Entry(new DateTime(2026, 8, 4), e =>
        {
            e.Content = "Довгий день, але вдалий";
            e.Gratitude = "за тишу";
        });
        _mood = new MoodStatistics
        {
            EmotionCounts = { [Emotion.Happy] = 2, [Emotion.Calm] = 2, [Emotion.Sad] = 2 }
        };

        var first = SnapshotSerializer.ToJson(await BuildAsync());
        var second = SnapshotSerializer.ToJson(await BuildAsync());

        second.Should().Be(first);
    }
}
