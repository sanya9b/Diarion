using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>Computed progress of a single budget for a given month.</summary>
public class BudgetProgress
{
    public Budget Budget { get; init; } = new();
    public decimal Spent { get; init; }
    public decimal Limit { get; init; }
    public decimal Remaining => Limit - Spent;
    public bool IsOverspent => Spent > Limit;

    /// <summary>Fraction spent, clamped to [0, 1] for a progress bar.</summary>
    public double Progress => Limit <= 0 ? 0 : Math.Min(1.0, (double)(Spent / Limit));

    /// <summary>Raw fraction spent (can exceed 1), for a "%" label.</summary>
    public double Fraction => Limit <= 0 ? 0 : (double)(Spent / Limit);
}

/// <summary>
/// Pure budgeting maths: how much has been spent against each category budget in a given month.
/// Deterministic; category matching is case-insensitive and only <see cref="TransactionType.Expense"/>
/// transactions count.
/// </summary>
public static class BudgetCalculator
{
    public static List<BudgetProgress> Compute(
        IEnumerable<Budget> budgets,
        IEnumerable<FinanceTransaction> transactions,
        DateTime month)
    {
        var result = new List<BudgetProgress>();
        if (budgets == null) return result;

        var tx = (transactions ?? Enumerable.Empty<FinanceTransaction>())
            .Where(t => t.Type == TransactionType.Expense
                        && t.Date.Year == month.Year
                        && t.Date.Month == month.Month)
            .ToList();

        foreach (var budget in budgets)
        {
            var spent = tx
                .Where(t => string.Equals(t.Category ?? string.Empty, budget.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);

            result.Add(new BudgetProgress
            {
                Budget = budget,
                Spent = spent,
                Limit = budget.MonthlyLimit
            });
        }

        return result;
    }
}
