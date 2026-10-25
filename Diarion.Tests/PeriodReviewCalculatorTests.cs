using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Services.Reports;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The review is arithmetic over two snapshots, and the arithmetic has one rule worth guarding
/// above the rest: a period that logged nothing is unknown, never zero. Most tests here are that
/// rule applied to one metric at a time.
/// </summary>
public class PeriodReviewCalculatorTests
{
    private static readonly StatsRange Week = new(new DateTime(2026, 10, 5), new DateTime(2026, 10, 11));
    private static readonly StatsRange Before = new(new DateTime(2026, 9, 28), new DateTime(2026, 10, 4));

    private static PeriodSnapshot Empty() => new();

    private static string Day(int offset) => Week.Start.AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public void Days_written_counts_text_mood_and_sleep_once_per_date()
    {
        var current = new PeriodSnapshot
        {
            Days = new List<SnapshotDay>
            {
                new() { Date = Day(0), Gratitude = "coffee" },
                new() { Date = Day(1) },
                new() { Date = Day(2), SleepNotes = "late" }
            },
            Mood = new SnapshotMood
            {
                Daily = new List<SnapshotMoodDay>
                {
                    new() { Date = Day(0), Valence = 1 },   // same day as the gratitude line
                    new() { Date = Day(3), Valence = -1 },
                    new() { Date = Day(4), Valence = null }
                }
            },
            Sleep = new SnapshotSleep
            {
                Daily = new List<SnapshotSleepDay>
                {
                    new() { Date = Day(5), Hours = 7.5 },
                    new() { Date = Day(6), Hours = null }
                }
            }
        };

        var review = PeriodReviewCalculator.Compare(current, Empty(), Week, Before);

        review.DayCount.Should().Be(7);
        review.DaysWritten.Current.Should().Be(4, "days 0, 2, 3 and 5 have something on them");
        review.DaysWritten.Previous.Should().Be(0, "a count is always known, even when it is zero");
    }

    [Fact]
    public void Mood_is_the_mean_valence_over_days_that_have_one_and_unknown_otherwise()
    {
        var current = new PeriodSnapshot
        {
            Mood = new SnapshotMood
            {
                Daily = new List<SnapshotMoodDay>
                {
                    new() { Date = Day(0), Valence = 2 },
                    new() { Date = Day(1), Valence = null },
                    new() { Date = Day(2), Valence = -1 }
                },
                TopEmotion = "Happy"
            }
        };

        var review = PeriodReviewCalculator.Compare(current, Empty(), Week, Before);

        review.Mood.Current.Should().BeApproximately(0.5, 1e-9);
        review.Mood.Previous.Should().BeNull("a week with no mood logged is not a week of neutral mood");
        review.Mood.Delta.Should().BeNull();
        review.TopEmotion.Should().Be("Happy");
    }

    [Fact]
    public void Sleep_averages_only_the_nights_that_were_logged()
    {
        var current = new PeriodSnapshot
        {
            Sleep = new SnapshotSleep
            {
                AverageHours = 3.0, // the statistics layer's own average, which counts blank nights as zero
                Daily = new List<SnapshotSleepDay>
                {
                    new() { Date = Day(0), Hours = 8, Quality = 4 },
                    new() { Date = Day(1), Hours = null, Quality = null },
                    new() { Date = Day(2), Hours = 6, Quality = 2 }
                }
            }
        };

        var review = PeriodReviewCalculator.Compare(current, Empty(), Week, Before);

        review.SleepHours.Current.Should().Be(7, "the blank night is not a night of zero sleep");
        review.SleepQuality.Current.Should().Be(3);
    }

    [Fact]
    public void Habit_completion_weighs_habits_by_how_often_they_were_due()
    {
        var current = new PeriodSnapshot
        {
            Habits = new SnapshotHabits
            {
                Good = new List<SnapshotHabit>
                {
                    new() { Name = "Water", CompletedDays = 7, ScheduledDays = 7 },
                    new() { Name = "Gym", CompletedDays = 0, ScheduledDays = 1 },
                    new() { Name = "Retired", CompletedDays = 0, ScheduledDays = 0 }
                }
            }
        };

        var review = PeriodReviewCalculator.Compare(current, Empty(), Week, Before);

        review.HabitCompletion.Current.Should().BeApproximately(7.0 / 8.0, 1e-9,
            "seven of eight due ticks, not the average of 100% and 0%");
        review.HabitCompletion.Previous.Should().BeNull("nothing was due in an empty period");
        review.Habits.Select(h => h.Name).Should().Equal("Gym", "Water");
        review.Habits.Should().OnlyContain(h => h.ScheduledDays > 0, "a habit that was never due has nothing to review");
    }

    [Fact]
    public void Habit_rows_find_their_previous_period_by_name_and_say_so_when_there_is_none()
    {
        var current = new PeriodSnapshot
        {
            Habits = new SnapshotHabits
            {
                Good = new List<SnapshotHabit>
                {
                    new() { Name = "Water", CompletedDays = 5, ScheduledDays = 7 },
                    new() { Name = "Reading", CompletedDays = 2, ScheduledDays = 3 }
                }
            }
        };
        var previous = new PeriodSnapshot
        {
            Habits = new SnapshotHabits
            {
                Good = new List<SnapshotHabit> { new() { Name = "Water", CompletedDays = 3, ScheduledDays = 7 } }
            }
        };

        var review = PeriodReviewCalculator.Compare(current, previous, Week, Before);

        var water = review.Habits.Single(h => h.Name == "Water");
        water.PreviousCompletedDays.Should().Be(3);
        water.PreviousRatio.Should().BeApproximately(3.0 / 7.0, 1e-9);

        var reading = review.Habits.Single(h => h.Name == "Reading");
        reading.PreviousCompletedDays.Should().BeNull("the habit did not exist last period");
        reading.PreviousRatio.Should().BeNull();
    }

    [Fact]
    public void Tasks_are_unknown_when_the_period_had_none()
    {
        var withTasks = new PeriodSnapshot { Tasks = new SnapshotTasks { Completed = 3, Total = 5 } };

        var review = PeriodReviewCalculator.Compare(withTasks, Empty(), Week, Before);

        review.TasksCompleted.Current.Should().Be(3);
        review.TasksTotal.Current.Should().Be(5);
        review.TasksCompleted.Previous.Should().BeNull("zero tasks done out of zero tasks is not a failure");
    }

    [Fact]
    public void Money_is_unknown_when_nothing_moved_in_either_direction()
    {
        var spent = new PeriodSnapshot { Finance = new SnapshotFinance { Expense = 1200.50m, Income = 0 } };

        var review = PeriodReviewCalculator.Compare(spent, Empty(), Week, Before);

        review.Expense.Current.Should().Be(1200.50);
        review.Income.Current.Should().Be(0, "income is a real zero once the period has any money in it");
        review.Expense.Previous.Should().BeNull("a period with no transactions did not spend nothing — it logged nothing");
        review.Expense.Delta.Should().BeNull();
    }

    [Fact]
    public void Delta_exists_only_when_both_halves_do()
    {
        var current = new PeriodSnapshot { Tasks = new SnapshotTasks { Completed = 4, Total = 4 } };
        var previous = new PeriodSnapshot { Tasks = new SnapshotTasks { Completed = 1, Total = 6 } };

        var review = PeriodReviewCalculator.Compare(current, previous, Week, Before);

        review.TasksCompleted.Delta.Should().Be(3);
        review.TasksTotal.Delta.Should().Be(-2);
    }

    [Fact]
    public void Empty_means_nothing_at_all_in_the_current_period()
    {
        PeriodReviewCalculator.Compare(Empty(), Empty(), Week, Before).IsEmpty.Should().BeTrue();

        var habitsSetUpButUntouched = new PeriodSnapshot
        {
            Habits = new SnapshotHabits
            {
                Good = new List<SnapshotHabit> { new() { Name = "Water", CompletedDays = 0, ScheduledDays = 7 } }
            }
        };
        PeriodReviewCalculator.Compare(habitsSetUpButUntouched, Empty(), Week, Before).IsEmpty
            .Should().BeTrue("habits that exist but were never ticked are not a start");

        var oneLine = new PeriodSnapshot
        {
            Days = new List<SnapshotDay> { new() { Date = Day(0), SoulFood = "walked" } }
        };
        PeriodReviewCalculator.Compare(oneLine, Empty(), Week, Before).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Ranges_are_normalized_and_kept()
    {
        var inverted = new StatsRange(Week.End.AddHours(5), Week.Start.AddHours(9));

        var review = PeriodReviewCalculator.Compare(Empty(), Empty(), inverted, Before);

        review.Range.Should().Be(Week);
        review.PreviousRange.Should().Be(Before);
        review.DayCount.Should().Be(7);
    }
}
