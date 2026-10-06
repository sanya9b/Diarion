using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class RecurrencePostingPlannerTests
{
    private static readonly DateTime Today = new(2026, 7, 15);

    private static RecurringTransaction DailyRule(
        DateTime lastPostedThrough,
        bool autoPost = true,
        bool paused = false) => new()
        {
            Type = TransactionType.Expense,
            Amount = 100m,
            Category = "Coffee",
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily },
            AutoPost = autoPost,
            IsPaused = paused,
            LastPostedThrough = lastPostedThrough
        };

    private static PostingPlan Plan(
        RecurringTransaction rule,
        IEnumerable<FinanceTransaction>? existing = null,
        Guid? fallbackAccountId = null)
        => RecurrencePostingPlanner.Plan(
            new[] { rule },
            existing ?? Enumerable.Empty<FinanceTransaction>(),
            Today,
            fallbackAccountId);

    [Fact]
    public void Plan_AutoPostRule_PostsEveryOccurrenceInTheWindow()
    {
        var plan = Plan(DailyRule(Today.AddDays(-3)));

        plan.ToPost.Select(t => t.Date)
            .Should().Equal(Today.AddDays(-2), Today.AddDays(-1), Today);
    }

    [Fact]
    public void Plan_RunTwiceWithTheAdvancedWatermark_PostsNothingTheSecondTime()
    {
        var rule = DailyRule(Today.AddDays(-3));

        var first = Plan(rule);
        first.ToPost.Should().HaveCount(3);

        // What the service writes back before the next load.
        rule.LastPostedThrough = first.Watermarks[rule.Id];

        Plan(rule, first.ToPost).ToPost.Should().BeEmpty();
    }

    [Fact]
    public void Plan_WithAResetWatermarkButExistingPostedRows_DoesNotDuplicate()
    {
        // The watermark is gone — a restored backup, or a hand-edited database. The rows themselves are
        // the second, independent guard.
        var rule = DailyRule(Today.AddDays(-3));
        var already = new[]
        {
            new FinanceTransaction { Date = Today.AddDays(-2), RecurringTransactionId = rule.Id },
            new FinanceTransaction { Date = Today.AddDays(-1), RecurringTransactionId = rule.Id }
        };

        Plan(rule, already).ToPost.Select(t => t.Date).Should().Equal(Today);
    }

    [Fact]
    public void Plan_IgnoresRowsBelongingToAnotherRule()
    {
        var rule = DailyRule(Today.AddDays(-1));
        var otherRulesRow = new FinanceTransaction { Date = Today, RecurringTransactionId = Guid.NewGuid() };

        Plan(rule, new[] { otherRulesRow }).ToPost.Should().HaveCount(1);
    }

    [Fact]
    public void Plan_IgnoresHandEnteredRowsOnTheSameDay()
    {
        // A manual row has no rule id, so it must not be mistaken for an occurrence already posted.
        var rule = DailyRule(Today.AddDays(-1));
        var manual = new FinanceTransaction { Date = Today, Amount = 100m, Category = "Coffee" };

        Plan(rule, new[] { manual }).ToPost.Should().HaveCount(1);
    }

    [Fact]
    public void Plan_NeverEmitsFutureDatedTransactions()
    {
        // BudgetCalculator filters by calendar month with no upper bound at today, so a future-dated row
        // would silently eat this month's budget.
        var plan = Plan(DailyRule(Today.AddDays(-5)));

        plan.ToPost.Should().OnlyContain(t => t.Date <= Today);
    }

    [Fact]
    public void Plan_NonAutoPostRule_ProducesPendingAndDoesNotAdvanceTheWatermark()
    {
        var rule = DailyRule(Today.AddDays(-2), autoPost: false);

        var plan = Plan(rule);

        plan.ToPost.Should().BeEmpty();
        plan.Pending.Select(p => p.Date).Should().Equal(Today.AddDays(-1), Today);
        plan.Watermarks.Should().NotContainKey(rule.Id);
    }

    [Fact]
    public void Plan_PendingOccurrences_AreOldestFirst()
    {
        // Confirming only ever moves the watermark forward, so the order the user is offered them matters.
        var plan = Plan(DailyRule(Today.AddDays(-3), autoPost: false));

        plan.Pending.Select(p => p.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Plan_PausedRule_ProducesNothingAndDoesNotAdvanceTheWatermark()
    {
        var rule = DailyRule(Today.AddDays(-5), paused: true);

        var plan = Plan(rule);

        plan.ToPost.Should().BeEmpty();
        plan.Pending.Should().BeEmpty();
        plan.Watermarks.Should().BeEmpty();
    }

    [Fact]
    public void Plan_AutoPostRuleWithNoOccurrencesInTheWindow_StillAdvancesTheWatermark()
    {
        // Without this, every load would re-scan the same empty window forever.
        var rule = new RecurringTransaction
        {
            Amount = 50m,
            Recurrence = new RecurrenceRule
            {
                Kind = RecurrenceKind.Weekly,
                DaysOfWeek = new List<int> { (int)DayOfWeek.Sunday }
            },
            LastPostedThrough = Today.AddDays(-2) // Mon 13th and Tue 14th — no Sunday in between
        };

        var plan = Plan(rule);

        plan.ToPost.Should().BeEmpty();
        plan.Watermarks[rule.Id].Should().Be(Today);
    }

    [Fact]
    public void Plan_WithAnAncientWatermark_ClampsTheWindowToTheBackfillCap()
    {
        var rule = DailyRule(new DateTime(2015, 1, 1));

        var plan = Plan(rule);

        plan.ToPost.Should().HaveCount(RecurrencePostingPlanner.MaxBackfillDays + 1);
        plan.ToPost.Min(t => t.Date).Should().Be(Today.AddDays(-RecurrencePostingPlanner.MaxBackfillDays));
    }

    [Fact]
    public void Plan_MonthlyRuleAfterALongGap_StillFires()
    {
        // The cap has to be wide enough that a monthly bill is never skipped entirely.
        var rule = new RecurringTransaction
        {
            Amount = 8000m,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.MonthlyByDay, DayOfMonth = 1 },
            LastPostedThrough = new DateTime(2015, 1, 1)
        };

        Plan(rule).ToPost.Should().NotBeEmpty();
    }

    [Fact]
    public void Plan_NewRuleAnchoredInTheFuture_PostsNothing()
    {
        var rule = new RecurringTransaction
        {
            Amount = 10m,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = Today.AddDays(10) },
            LastPostedThrough = Today.AddDays(-5)
        };

        Plan(rule).ToPost.Should().BeEmpty();
    }

    [Fact]
    public void Plan_RuleThatEndedLastMonth_PostsNothing()
    {
        var rule = new RecurringTransaction
        {
            Amount = 10m,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, EndDate = Today.AddDays(-30) },
            LastPostedThrough = Today.AddDays(-10)
        };

        Plan(rule).ToPost.Should().BeEmpty();
    }

    [Fact]
    public void Plan_RuleWatermarkedToday_PostsNothing()
    {
        Plan(DailyRule(Today)).ToPost.Should().BeEmpty();
    }

    [Fact]
    public void Plan_PostedTransaction_CopiesTheRuleAndCarriesItsId()
    {
        var accountId = Guid.NewGuid();
        var rule = new RecurringTransaction
        {
            Type = TransactionType.Income,
            AccountId = accountId,
            Amount = 20000m,
            Category = "Salary",
            Note = "monthly",
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily },
            LastPostedThrough = Today.AddDays(-1)
        };

        var posted = Plan(rule).ToPost.Should().ContainSingle().Subject;

        posted.Type.Should().Be(TransactionType.Income);
        posted.AccountId.Should().Be(accountId);
        posted.Amount.Should().Be(20000m);
        posted.Category.Should().Be("Salary");
        posted.Note.Should().Be("monthly");
        posted.Date.Should().Be(Today);
        posted.RecurringTransactionId.Should().Be(rule.Id);
    }

    [Fact]
    public void Plan_RuleWithNoAccount_UsesTheFallbackAccount()
    {
        var fallback = Guid.NewGuid();
        var rule = DailyRule(Today.AddDays(-1));
        rule.AccountId = null;

        Plan(rule, fallbackAccountId: fallback).ToPost.Single().AccountId.Should().Be(fallback);
    }

    [Fact]
    public void Plan_WithNoRules_ReturnsAnEmptyPlan()
    {
        var plan = RecurrencePostingPlanner.Plan(
            Array.Empty<RecurringTransaction>(), Array.Empty<FinanceTransaction>(), Today);

        plan.ToPost.Should().BeEmpty();
        plan.Pending.Should().BeEmpty();
        plan.Watermarks.Should().BeEmpty();
    }

    [Fact]
    public void Plan_MixedRules_HandlesEachIndependently()
    {
        var auto = DailyRule(Today.AddDays(-1));
        var manual = DailyRule(Today.AddDays(-1), autoPost: false);
        var paused = DailyRule(Today.AddDays(-1), paused: true);

        var plan = RecurrencePostingPlanner.Plan(
            new[] { auto, manual, paused }, Array.Empty<FinanceTransaction>(), Today);

        plan.ToPost.Should().ContainSingle().Which.RecurringTransactionId.Should().Be(auto.Id);
        plan.Pending.Should().ContainSingle().Which.RuleId.Should().Be(manual.Id);
        plan.Watermarks.Should().ContainKey(auto.Id).And.NotContainKey(paused.Id);
    }
}
