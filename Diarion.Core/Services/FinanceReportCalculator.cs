using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>One bar of the trend chart: what came in and what went out over a stretch of days.</summary>
public sealed class FinancePeriodBucket
{
    public DateTime Start { get; init; }
    /// <summary>Inclusive.</summary>
    public DateTime End { get; init; }
    public ReportBucketUnit Unit { get; init; }

    /// <summary>Clipped by the window at one end, so its bar covers fewer days than its neighbours and
    /// is not comparable to them. The view dims these rather than letting a short bar read as a drop.</summary>
    public bool IsPartial { get; init; }

    public decimal Income { get; init; }
    public decimal Expense { get; init; }

    public decimal Net => Income - Expense;
    public bool IsEmpty => Income == 0m && Expense == 0m;
}

public sealed class FinanceTrendReport
{
    public IReadOnlyList<FinancePeriodBucket> Buckets { get; init; } = Array.Empty<FinancePeriodBucket>();
    public ReportBucketUnit Unit { get; init; }

    public decimal TotalIncome => Buckets.Sum(b => b.Income);
    public decimal TotalExpense => Buckets.Sum(b => b.Expense);

    /// <summary>
    /// The largest single-sided value across all buckets. A diverging chart must scale <b>both</b> halves
    /// to this one number — scaled independently, 50 000 of income and 3 000 of expense draw the same
    /// height and the picture lies.
    /// </summary>
    public decimal PeakMagnitude => Buckets.Count == 0
        ? 0m
        : Buckets.Max(b => Math.Max(b.Income, b.Expense));

    /// <summary>Two points are not a trend. Below this the card should not be drawn at all.</summary>
    public bool IsMeaningful => Buckets.Count >= FinanceReportCalculator.MinBucketsForTrend;

    public bool HasAnyData => Buckets.Any(b => !b.IsEmpty);
}

/// <summary>One figure measured against the same figure in the preceding window.</summary>
public sealed class FinanceMetricDelta
{
    public decimal Current { get; init; }
    public decimal Previous { get; init; }

    public decimal Change => Current - Previous;

    /// <summary>
    /// Change as a fraction of the baseline's <b>magnitude</b>. Null when there was no baseline but there
    /// is something now: that growth is undefined, and both "+100%" and "+∞%" would be inventions — the
    /// view shows a "new" badge instead. Zero when both sides are zero, which is a real "unchanged".
    ///
    /// The absolute value in the denominator is load-bearing for <see cref="FinanceComparisonReport.Net"/>,
    /// which can be negative: a net of −100 improving to −50 is a 50% improvement, but dividing by the
    /// signed baseline reports −50%, i.e. exactly backwards.
    /// </summary>
    public double? Fraction => Previous == 0m
        ? (Current == 0m ? 0d : (double?)null)
        : (double)Change / (double)Math.Abs(Previous);

    public bool IsNew => Previous == 0m && Current != 0m;
    public bool IsIncrease => Change > 0m;
    public bool IsUnchanged => Change == 0m;
}

/// <summary>A category that moved between the two windows.</summary>
public sealed class FinanceCategoryMover
{
    /// <summary>Empty means uncategorised; the view supplies the localized label.</summary>
    public string Category { get; init; } = string.Empty;
    public TransactionType Type { get; init; }

    public decimal Current { get; init; }
    public decimal Previous { get; init; }

    public decimal Change => Current - Previous;

    /// <summary>Same rule as <see cref="FinanceMetricDelta.Fraction"/>.</summary>
    public double? Fraction => Previous == 0m
        ? (Current == 0m ? 0d : (double?)null)
        : (double)Change / (double)Math.Abs(Previous);

    public bool IsNew => Previous == 0m && Current != 0m;
    /// <summary>Spent on last time, not at all this time — worth saying out loud, not just as a decrease.</summary>
    public bool IsGone => Current == 0m && Previous != 0m;
    public bool IsIncrease => Change > 0m;
}

public sealed class FinanceComparisonReport
{
    public DateTime CurrentStart { get; init; }
    public DateTime CurrentEnd { get; init; }
    public DateTime PreviousStart { get; init; }
    public DateTime PreviousEnd { get; init; }
    public int Days { get; init; }

    public FinanceMetricDelta Income { get; init; } = new();
    public FinanceMetricDelta Expense { get; init; } = new();

    /// <remarks>Read <see cref="FinanceMetricDelta.Change"/>, not <c>Fraction</c>. A net that crosses zero
    /// produces a percentage that is arithmetically fine and cognitively useless.</remarks>
    public FinanceMetricDelta Net { get; init; } = new();

    public IReadOnlyList<FinanceCategoryMover> ExpenseMovers { get; init; } = Array.Empty<FinanceCategoryMover>();
    public IReadOnlyList<FinanceCategoryMover> IncomeMovers { get; init; } = Array.Empty<FinanceCategoryMover>();

    /// <summary>False when nothing at all happened in the preceding window. Every row would then read
    /// "new" and every percentage would be null, so the card says "no earlier data" instead.</summary>
    public bool HasBaseline => Income.Previous != 0m || Expense.Previous != 0m;
}

/// <summary>What one account did over the window. Flow, not standing balance.</summary>
public sealed class FinanceAccountReportRow
{
    /// <summary>Null on the balancing row that absorbs unassigned and orphaned transactions.</summary>
    public Guid? AccountId { get; init; }

    /// <summary>Carried whole so the view can resolve the localized name, icon and colour. Resolving the
    /// name here would read the current culture, which does not belong in a calculator.</summary>
    public Account? Account { get; init; }

    public bool IsUnassigned => AccountId == null;
    public bool IsArchived => Account?.IsArchived == true;

    /// <summary>Excludes transfers — they are not income (see <see cref="Transfer"/>).</summary>
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public decimal Net => Income - Expense;

    public decimal TransferIn { get; init; }
    public decimal TransferOut { get; init; }

    /// <summary>Kept as two legs rather than only the net: "net zero" hides a large sum churning through.</summary>
    public decimal TransferNet => TransferIn - TransferOut;

    public bool HasTransfers => TransferIn != 0m || TransferOut != 0m;
    public bool HasActivity => Income != 0m || Expense != 0m || HasTransfers;
}

/// <summary>
/// The three finance reports, as pure functions of the rows and the window. No I/O, no culture, no
/// formatting — same contract as <see cref="BudgetCalculator"/> and <see cref="AccountBalanceCalculator"/>.
/// </summary>
public static class FinanceReportCalculator
{
    public const int MinBucketsForTrend = 3;
    public const int DefaultTopMovers = 3;

    public static FinanceTrendReport ComputeTrend(
        IEnumerable<FinanceTransaction> transactions,
        DateTime start,
        DateTime end)
    {
        var rows = (transactions ?? Enumerable.Empty<FinanceTransaction>()).ToList();
        var days = (end.Date - start.Date).Days + 1;
        var unit = ReportPeriod.ChooseUnit(days);

        var buckets = ReportPeriod.Buckets(start, end, unit)
            .Select(range =>
            {
                var inRange = rows.Where(t => t.Date.Date >= range.Start && t.Date.Date <= range.End).ToList();
                return new FinancePeriodBucket
                {
                    Start = range.Start,
                    End = range.End,
                    Unit = unit,
                    IsPartial = range.IsPartial,
                    Income = inRange.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                    Expense = inRange.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                };
            })
            .ToList();

        return new FinanceTrendReport { Buckets = buckets, Unit = unit };
    }

    public static FinanceComparisonReport ComputeComparison(
        IEnumerable<FinanceTransaction> transactions,
        DateTime currentStart,
        DateTime currentEnd,
        int topMovers = DefaultTopMovers)
    {
        var rows = (transactions ?? Enumerable.Empty<FinanceTransaction>()).ToList();
        var (previousStart, previousEnd) = ReportPeriod.PreviousWindow(currentStart, currentEnd);

        var current = InWindow(rows, currentStart, currentEnd);
        var previous = InWindow(rows, previousStart, previousEnd);

        var currentIncome = Sum(current, TransactionType.Income);
        var currentExpense = Sum(current, TransactionType.Expense);
        var previousIncome = Sum(previous, TransactionType.Income);
        var previousExpense = Sum(previous, TransactionType.Expense);

        return new FinanceComparisonReport
        {
            CurrentStart = currentStart.Date,
            CurrentEnd = currentEnd.Date,
            PreviousStart = previousStart,
            PreviousEnd = previousEnd,
            Days = (currentEnd.Date - currentStart.Date).Days + 1,
            Income = new FinanceMetricDelta { Current = currentIncome, Previous = previousIncome },
            Expense = new FinanceMetricDelta { Current = currentExpense, Previous = previousExpense },
            Net = new FinanceMetricDelta
            {
                Current = currentIncome - currentExpense,
                Previous = previousIncome - previousExpense
            },
            ExpenseMovers = Movers(current, previous, TransactionType.Expense, topMovers),
            IncomeMovers = Movers(current, previous, TransactionType.Income, topMovers)
        };
    }

    public static List<FinanceAccountReportRow> ComputeAccountBreakdown(
        IEnumerable<Account> accounts,
        IEnumerable<FinanceTransaction> transactions,
        IEnumerable<Transfer>? transfers,
        DateTime start,
        DateTime end)
    {
        var from = start.Date;
        var to = end.Date;
        var known = (accounts ?? Enumerable.Empty<Account>()).ToList();
        var knownIds = known.Select(a => a.Id).ToHashSet();

        var rows = InWindow((transactions ?? Enumerable.Empty<FinanceTransaction>()).ToList(), from, to);

        // Transfer.Date is the one finance date without a truncating setter (see Transfer.cs), so the
        // comparison has to truncate here. Written as `t.Date <= to` against a midnight bound it would
        // silently drop every transfer made today.
        var moves = (transfers ?? Enumerable.Empty<Transfer>())
            .Where(t => t.Date.Date >= from && t.Date.Date <= to)
            .ToList();

        var result = new List<FinanceAccountReportRow>();
        foreach (var account in known)
        {
            var own = rows.Where(t => t.AccountId == account.Id).ToList();
            result.Add(new FinanceAccountReportRow
            {
                AccountId = account.Id,
                Account = account,
                Income = Sum(own, TransactionType.Income),
                Expense = Sum(own, TransactionType.Expense),
                TransferIn = moves.Where(t => t.ToAccountId == account.Id).Sum(t => t.Amount),
                TransferOut = moves.Where(t => t.FromAccountId == account.Id).Sum(t => t.Amount)
            });
        }

        // Everything the accounts above did not claim — rows with no account, and rows pointing at an
        // account that no longer exists — lands in one balancing row. Without it the per-account figures
        // would quietly fail to add up to the totals shown above them, and a row nobody can see is a row
        // nobody can fix.
        var orphanRows = rows.Where(t => t.AccountId == null || !knownIds.Contains(t.AccountId.Value)).ToList();
        var orphanIn = moves.Where(t => !knownIds.Contains(t.ToAccountId)).Sum(t => t.Amount);
        var orphanOut = moves.Where(t => !knownIds.Contains(t.FromAccountId)).Sum(t => t.Amount);

        if (orphanRows.Count > 0 || orphanIn != 0m || orphanOut != 0m)
        {
            result.Add(new FinanceAccountReportRow
            {
                AccountId = null,
                Account = null,
                Income = Sum(orphanRows, TransactionType.Income),
                Expense = Sum(orphanRows, TransactionType.Expense),
                TransferIn = orphanIn,
                TransferOut = orphanOut
            });
        }

        // An archived account with nothing in the window is noise; an active one at zero is an answer.
        return result
            .Where(r => !r.IsArchived || r.HasActivity)
            .OrderBy(r => r.IsUnassigned)
            .ThenByDescending(r => r.Expense)
            .ThenByDescending(r => r.Income)
            .ThenBy(r => r.Account?.CreatedAt ?? DateTime.MaxValue)
            .ToList();
    }

    private static List<FinanceTransaction> InWindow(List<FinanceTransaction> rows, DateTime start, DateTime end)
        => rows.Where(t => t.Date.Date >= start.Date && t.Date.Date <= end.Date).ToList();

    private static decimal Sum(IEnumerable<FinanceTransaction> rows, TransactionType type)
        => rows.Where(t => t.Type == type).Sum(t => t.Amount);

    /// <summary>
    /// Categories ranked by how much they moved, rises and falls in one list — a category that dropped
    /// 500 is more newsworthy than one that rose 400, and splitting them forces a comparison across two
    /// sorts. Categories that did not move are absent: the card is about movement.
    /// </summary>
    private static List<FinanceCategoryMover> Movers(
        List<FinanceTransaction> current,
        List<FinanceTransaction> previous,
        TransactionType type,
        int take)
    {
        var currentByCategory = ByCategory(current, type);
        var previousByCategory = ByCategory(previous, type);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in previousByCategory.Keys) names[key] = key;
        foreach (var key in currentByCategory.Keys) names[key] = key; // current window wins the casing

        return names.Keys
            .Select(key => new FinanceCategoryMover
            {
                Category = names[key],
                Type = type,
                Current = currentByCategory.TryGetValue(key, out var c) ? c : 0m,
                Previous = previousByCategory.TryGetValue(key, out var p) ? p : 0m
            })
            .Where(m => m.Change != 0m)
            // Total ordering, so the result cannot depend on input order.
            .OrderByDescending(m => Math.Abs(m.Change))
            .ThenByDescending(m => m.Current)
            .ThenBy(m => m.Category, StringComparer.Ordinal)
            .Take(Math.Max(0, take))
            .ToList();
    }

    private static Dictionary<string, decimal> ByCategory(List<FinanceTransaction> rows, TransactionType type)
        => rows
            .Where(t => t.Type == type)
            .GroupBy(t => (t.Category ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount), StringComparer.OrdinalIgnoreCase);
}
