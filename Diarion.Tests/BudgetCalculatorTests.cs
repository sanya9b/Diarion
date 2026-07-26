using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class BudgetCalculatorTests
{
    private static readonly DateTime Month = new(2026, 7, 15);

    private static FinanceTransaction Expense(string cat, decimal amt, DateTime date) =>
        new() { Type = TransactionType.Expense, Category = cat, Amount = amt, Date = date };

    [Fact]
    public void Compute_SumsExpensesForCategoryInMonth_CaseInsensitive()
    {
        var budgets = new List<Budget> { new() { Category = "Food", MonthlyLimit = 100m } };
        var tx = new List<FinanceTransaction>
        {
            Expense("Food", 30m, new DateTime(2026, 7, 1)),
            Expense("food", 20m, new DateTime(2026, 7, 20)),                       // case-insensitive
            Expense("Food", 999m, new DateTime(2026, 6, 30)),                      // other month
            new() { Type = TransactionType.Income, Category = "Food", Amount = 500m, Date = new DateTime(2026, 7, 5) }, // income
            Expense("Transport", 40m, new DateTime(2026, 7, 10))                   // other category
        };

        var p = BudgetCalculator.Compute(budgets, tx, Month).Single();

        p.Spent.Should().Be(50m);
        p.Limit.Should().Be(100m);
        p.Remaining.Should().Be(50m);
        p.IsOverspent.Should().BeFalse();
        p.Progress.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Compute_Overspent_ClampsProgress_AndFlags()
    {
        var budgets = new List<Budget> { new() { Category = "Food", MonthlyLimit = 100m } };
        var tx = new List<FinanceTransaction> { Expense("Food", 150m, new DateTime(2026, 7, 3)) };

        var p = BudgetCalculator.Compute(budgets, tx, Month).Single();

        p.Spent.Should().Be(150m);
        p.IsOverspent.Should().BeTrue();
        p.Remaining.Should().Be(-50m);
        p.Progress.Should().Be(1.0);              // clamped for the bar
        p.Fraction.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void Compute_NoTransactions_ZeroSpent()
    {
        var budgets = new List<Budget> { new() { Category = "Food", MonthlyLimit = 100m } };

        var p = BudgetCalculator.Compute(budgets, new List<FinanceTransaction>(), Month).Single();

        p.Spent.Should().Be(0m);
        p.Progress.Should().Be(0);
        p.IsOverspent.Should().BeFalse();
    }
}
