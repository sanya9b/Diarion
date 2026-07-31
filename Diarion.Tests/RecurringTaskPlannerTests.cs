using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The day-planner for repeating tasks. Pure date and priority arithmetic — no database — so every guard
/// that decides whether a row appears is pinned here rather than through the service.
/// </summary>
public class RecurringTaskPlannerTests
{
    private static readonly DateTime Today = new(2026, 7, 15);

    private static RecurringTask DailyRule(string description = "Стретчинг", TodoPriority priority = TodoPriority.Medium)
        => new()
        {
            TaskDescription = description,
            Priority = priority,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = Today.AddDays(-30) },
            CreatedAt = new DateTime(2026, 1, 1)
        };

    private static List<TodoItem> Plan(IEnumerable<RecurringTask> rules, IEnumerable<TodoItem>? existing = null)
        => RecurringTaskPlanner.PlanForDay(rules, existing ?? new List<TodoItem>(), Today);

    [Fact]
    public void ADueRuleProducesARowCarryingTheTemplateAndTheRuleId()
    {
        var rule = DailyRule();
        rule.HasTime = true;
        rule.TargetTime = TimeSpan.FromHours(8);
        rule.HasReminder = true;

        var planned = Plan(new[] { rule }).Single();

        planned.RecurringTaskId.Should().Be(rule.Id);
        planned.TargetDate.Should().Be(Today);
        planned.TaskDescription.Should().Be("Стретчинг");
        planned.HasTime.Should().BeTrue();
        planned.TargetTime.Should().Be(TimeSpan.FromHours(8));
        planned.HasReminder.Should().BeTrue();
        planned.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void ARuleWhoseAnchorIsLaterProducesNothing()
    {
        var rule = DailyRule();
        rule.Recurrence.Anchor = Today.AddDays(1);

        Plan(new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void ARuleThatEndedBeforeTheDayProducesNothing()
    {
        var rule = DailyRule();
        rule.Recurrence.EndDate = Today.AddDays(-1);

        Plan(new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void AWeeklyRuleIsSilentOnAnOffDay()
    {
        var rule = DailyRule();
        rule.Recurrence.Kind = RecurrenceKind.Weekly;
        rule.Recurrence.DaysOfWeek = new List<int> { (int)Today.AddDays(1).DayOfWeek };

        Plan(new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void AWeeklyRuleFiresOnItsOwnWeekday()
    {
        var rule = DailyRule();
        rule.Recurrence.Kind = RecurrenceKind.Weekly;
        rule.Recurrence.DaysOfWeek = new List<int> { (int)Today.DayOfWeek };

        Plan(new[] { rule }).Should().ContainSingle();
    }

    [Fact]
    public void ASkippedDateProducesNothing()
    {
        // Deleting one row of a series means "not this day", because deleting cannot mean "delete the
        // task" — the rule would simply produce it again the next time this day was opened.
        var rule = DailyRule();
        rule.Skip(Today);

        Plan(new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void ASkipOnAnotherDayDoesNotSilenceThisOne()
    {
        var rule = DailyRule();
        rule.Skip(Today.AddDays(-1));

        Plan(new[] { rule }).Should().ContainSingle();
    }

    [Fact]
    public void AnOccurrenceAlreadyOnTheDayIsNotDuplicated()
    {
        var rule = DailyRule();
        var existing = new TodoItem { RecurringTaskId = rule.Id, TargetDate = Today, TaskDescription = "Стретчинг" };

        Plan(new[] { rule }, new[] { existing }).Should().BeEmpty();
    }

    [Fact]
    public void TwoRulesWithTheSameDescriptionBothProduceARow()
    {
        // The whole reason a series needed an identity. Grouped by description — as the scheme this
        // replaced did whenever the group id was empty — one of these two would silently swallow the other.
        var first = DailyRule("Прибрати");
        var second = DailyRule("Прибрати");

        var planned = Plan(new[] { first, second });

        planned.Should().HaveCount(2);
        planned.Select(t => t.RecurringTaskId).Should().BeEquivalentTo(new[] { (Guid?)first.Id, second.Id });
    }

    [Fact]
    public void AHandEnteredRowWithTheSameTextDoesNotSuppressTheOccurrence()
    {
        var rule = DailyRule("Прибрати");
        var handEntered = new TodoItem { TargetDate = Today, TaskDescription = "Прибрати" };

        Plan(new[] { rule }, new[] { handEntered }).Should().ContainSingle();
    }

    [Fact]
    public void AnArrivingHighIsDemotedWhenTheDayAlreadyHoldsThree()
    {
        var existing = Enumerable.Range(0, RecurringTaskPlanner.MaxHighPriorityPerDay)
            .Select(_ => new TodoItem { TargetDate = Today, Priority = TodoPriority.High })
            .ToList();

        Plan(new[] { DailyRule(priority: TodoPriority.High) }, existing)
            .Single().Priority.Should().Be(TodoPriority.Medium);
    }

    [Fact]
    public void CompletedHighTasksDoNotCountTowardsTheLimit()
    {
        var existing = Enumerable.Range(0, RecurringTaskPlanner.MaxHighPriorityPerDay)
            .Select(_ => new TodoItem { TargetDate = Today, Priority = TodoPriority.High, IsCompleted = true })
            .ToList();

        Plan(new[] { DailyRule(priority: TodoPriority.High) }, existing)
            .Single().Priority.Should().Be(TodoPriority.High);
    }

    [Fact]
    public void WhenTwoHighRulesCompeteForOneSlotTheYoungerOneIsDemoted()
    {
        // Left to the order the database happens to return, which of the two loses would be arbitrary —
        // and arbitrary in a way that reads as a bug only on the day it changes.
        var older = DailyRule("Раніше", TodoPriority.High);
        older.CreatedAt = new DateTime(2026, 1, 1);
        var younger = DailyRule("Пізніше", TodoPriority.High);
        younger.CreatedAt = new DateTime(2026, 5, 1);

        var existing = Enumerable.Range(0, RecurringTaskPlanner.MaxHighPriorityPerDay - 1)
            .Select(_ => new TodoItem { TargetDate = Today, Priority = TodoPriority.High })
            .ToList();

        var planned = Plan(new[] { younger, older }, existing);

        planned.Single(t => t.TaskDescription == "Раніше").Priority.Should().Be(TodoPriority.High);
        planned.Single(t => t.TaskDescription == "Пізніше").Priority.Should().Be(TodoPriority.Medium);
    }

    [Fact]
    public void NoRulesMeansNoRows()
    {
        Plan(Array.Empty<RecurringTask>()).Should().BeEmpty();
    }
}
