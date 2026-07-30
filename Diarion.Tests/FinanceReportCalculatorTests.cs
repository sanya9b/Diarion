using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class FinanceReportCalculatorTests
{
    private static readonly DateTime Today = new(2026, 7, 30);

    private static DateTime Start(int days) => Today.AddDays(-(days - 1));

    private static FinanceTransaction Tx(
        TransactionType type,
        decimal amount,
        DateTime date,
        string category = "Food",
        Guid? accountId = null)
        => new() { Type = type, Amount = amount, Date = date, Category = category, AccountId = accountId };

    private static FinanceTransaction Spend(decimal amount, DateTime date, string category = "Food", Guid? accountId = null)
        => Tx(TransactionType.Expense, amount, date, category, accountId);

    private static FinanceTransaction Earn(decimal amount, DateTime date, string category = "Salary", Guid? accountId = null)
        => Tx(TransactionType.Income, amount, date, category, accountId);

    // --- Trend ---

    [Fact]
    public void ComputeTrend_EmptyMonthsInTheMiddle_StillProduceBuckets()
    {
        // Proves the sequence comes from the window bounds, not from a GroupBy over the rows. Grouped by
        // data, the quiet months would silently vanish and the chart would compress time.
        var rows = new[]
        {
            Spend(100m, Start(180)),
            Spend(200m, Today)
        };

        var report = FinanceReportCalculator.ComputeTrend(rows, Start(180), Today);

        // 180 days back from 30 Jul 2026 is exactly 1 Feb, so the window is Feb through Jul.
        report.Unit.Should().Be(ReportBucketUnit.Month);
        report.Buckets.Should().HaveCount(6);
        report.Buckets.Skip(1).Take(4).Should().OnlyContain(b => b.IsEmpty);
        report.HasAnyData.Should().BeTrue();
    }

    [Fact]
    public void ComputeTrend_NoTransactionsAtAll_StillProducesTheFullBucketSequence()
    {
        var report = FinanceReportCalculator.ComputeTrend(Array.Empty<FinanceTransaction>(), Start(90), Today);

        report.Buckets.Should().HaveCount(3);
        report.HasAnyData.Should().BeFalse();
        report.PeakMagnitude.Should().Be(0m);
    }

    [Fact]
    public void ComputeTrend_SplitsIncomeAndExpenseIntoTheRightBucket()
    {
        var rows = new[]
        {
            Earn(20000m, new DateTime(2026, 6, 5)),
            Spend(1200m, new DateTime(2026, 6, 20)),
            Spend(300m, Today),
            Spend(50m, new DateTime(2026, 4, 30))   // one day before the window
        };

        var report = FinanceReportCalculator.ComputeTrend(rows, Start(90), Today);

        var june = report.Buckets.Single(b => b.Start.Month == 6);
        june.Income.Should().Be(20000m);
        june.Expense.Should().Be(1200m);
        june.Net.Should().Be(18800m);

        report.TotalExpense.Should().Be(1500m);   // the April row is outside the window
    }

    [Fact]
    public void ComputeTrend_TotalsEqualTheRawWindowSums()
    {
        // Invariant: no clipping bug may eat a day at a bucket boundary.
        var rows = Enumerable.Range(0, 90)
            .Select(offset => Spend(10m, Start(90).AddDays(offset)))
            .ToList();

        var report = FinanceReportCalculator.ComputeTrend(rows, Start(90), Today);

        report.TotalExpense.Should().Be(rows.Sum(r => r.Amount));
    }

    [Fact]
    public void ComputeTrend_PeakMagnitudeIsTheLargestSingleSide_SoBothHalvesShareAnAxis()
    {
        var rows = new[]
        {
            Earn(50000m, new DateTime(2026, 6, 5)),
            Spend(3000m, new DateTime(2026, 6, 6))
        };

        var report = FinanceReportCalculator.ComputeTrend(rows, Start(90), Today);

        report.PeakMagnitude.Should().Be(50000m);
    }

    [Theory]
    [InlineData(7, ReportBucketUnit.Week, 1, false)]
    [InlineData(14, ReportBucketUnit.Week, 2, false)]
    [InlineData(30, ReportBucketUnit.Week, 5, true)]
    [InlineData(90, ReportBucketUnit.Month, 3, true)]
    [InlineData(180, ReportBucketUnit.Month, 6, true)]
    [InlineData(365, ReportBucketUnit.Month, 13, true)]
    public void ComputeTrend_BucketPolicy(int days, ReportBucketUnit unit, int count, bool meaningful)
    {
        var report = FinanceReportCalculator.ComputeTrend(Array.Empty<FinanceTransaction>(), Start(days), Today);

        report.Unit.Should().Be(unit);
        report.Buckets.Should().HaveCount(count);
        report.IsMeaningful.Should().Be(meaningful);
    }

    // --- Comparison ---

    [Fact]
    public void ComputeComparison_MeasuresAgainstTheImmediatelyPrecedingEqualWindow()
    {
        var rows = new[]
        {
            Spend(100m, Today),                        // current window
            Spend(50m, Start(30)),                     // current window, first day
            Spend(400m, Start(30).AddDays(-1)),        // previous window, last day
            Spend(70m, Start(30).AddDays(-30)),        // previous window, first day
            Spend(999m, Start(30).AddDays(-31))        // one day before the baseline
        };

        var report = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today);

        report.Expense.Current.Should().Be(150m);
        report.Expense.Previous.Should().Be(470m);
        report.Expense.Change.Should().Be(-320m);
        report.Days.Should().Be(30);
        report.HasBaseline.Should().BeTrue();
    }

    [Fact]
    public void ComputeComparison_PreviousZeroAndCurrentPositive_FractionIsNullAndIsNew()
    {
        // Not 0 — that would claim "unchanged" after going from nothing to 500. Not infinity either,
        // which renders as "∞ %" and is one step from NaN in a bar height.
        var report = FinanceReportCalculator.ComputeComparison(new[] { Spend(500m, Today) }, Start(30), Today);

        report.Expense.Fraction.Should().BeNull();
        report.Expense.IsNew.Should().BeTrue();
        report.Expense.Change.Should().Be(500m);
        report.HasBaseline.Should().BeFalse();
    }

    [Fact]
    public void ComputeComparison_BothWindowsEmpty_FractionIsZeroAndNotNew()
    {
        var report = FinanceReportCalculator.ComputeComparison(Array.Empty<FinanceTransaction>(), Start(30), Today);

        report.Expense.Fraction.Should().Be(0d);
        report.Expense.IsNew.Should().BeFalse();
        report.Expense.IsUnchanged.Should().BeTrue();
    }

    [Fact]
    public void ComputeComparison_NegativeNet_MeasuresAgainstTheAbsoluteBaseline()
    {
        // Net −100 improving to −50 is a 50% improvement. Dividing by the signed baseline reports −50%,
        // i.e. exactly backwards, and no test built from positive numbers would ever notice.
        var rows = new[]
        {
            Spend(50m, Today),                          // current: net −50
            Spend(100m, Start(30).AddDays(-1))          // previous: net −100
        };

        var report = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today);

        report.Net.Current.Should().Be(-50m);
        report.Net.Previous.Should().Be(-100m);
        report.Net.Change.Should().Be(50m);
        report.Net.Fraction.Should().BeApproximately(0.5d, 0.0001);
        report.Net.IsIncrease.Should().BeTrue();
    }

    [Fact]
    public void ComputeComparison_Movers_RankByAbsoluteChangeMixingRisesAndFalls()
    {
        var previousDay = Start(30).AddDays(-1);
        var rows = new[]
        {
            Spend(500m, previousDay, "Taxi"),      // gone entirely: −500
            Spend(400m, Today, "Cafe"),            // new: +400
            Spend(100m, previousDay, "Rent"),
            Spend(150m, Today, "Rent"),            // +50
            Spend(1000m, previousDay, "Bills"),
            Spend(1000m, Today, "Bills")           // unchanged — must not appear
        };

        var report = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today);

        report.ExpenseMovers.Select(m => m.Category).Should().Equal("Taxi", "Cafe", "Rent");
        report.ExpenseMovers[0].IsGone.Should().BeTrue();
        report.ExpenseMovers[1].IsNew.Should().BeTrue();
        report.ExpenseMovers.Should().NotContain(m => m.Category == "Bills");
    }

    [Fact]
    public void ComputeComparison_Movers_MergeCategoriesCaseInsensitively()
    {
        var rows = new[]
        {
            Spend(100m, Start(30).AddDays(-1), "Food"),
            Spend(60m, Today, "food"),
            Spend(40m, Today, "FOOD")
        };

        var report = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today);

        report.ExpenseMovers.Should().BeEmpty("100 before and 100 after is not a movement");
    }

    [Fact]
    public void ComputeComparison_Movers_AreDeterministicRegardlessOfInputOrder()
    {
        var previousDay = Start(30).AddDays(-1);
        var rows = new List<FinanceTransaction>
        {
            Spend(100m, Today, "Alpha"),
            Spend(100m, Today, "Beta"),      // identical magnitude — the tie-break must decide
            Spend(300m, previousDay, "Gamma")
        };

        var forward = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today, topMovers: 10);
        var reversed = FinanceReportCalculator.ComputeComparison(Enumerable.Reverse(rows), Start(30), Today, topMovers: 10);

        forward.ExpenseMovers.Select(m => m.Category)
               .Should().Equal(reversed.ExpenseMovers.Select(m => m.Category));
    }

    [Fact]
    public void ComputeComparison_IncomeAndExpenseMoversAreSeparate()
    {
        // One salary change must not crowd the expense movers out of the card.
        var previousDay = Start(30).AddDays(-1);
        var rows = new[]
        {
            Earn(20000m, previousDay, "Salary"),
            Earn(25000m, Today, "Salary"),
            Spend(100m, Today, "Cafe")
        };

        var report = FinanceReportCalculator.ComputeComparison(rows, Start(30), Today);

        report.IncomeMovers.Should().ContainSingle().Which.Category.Should().Be("Salary");
        report.ExpenseMovers.Should().ContainSingle().Which.Category.Should().Be("Cafe");
    }

    // --- Account breakdown ---

    private static Account Acc(string name, string colour = "#8FA083", bool archived = false)
        => new() { Name = name, ColorHex = colour, IsArchived = archived };

    [Fact]
    public void ComputeAccountBreakdown_PerAccountTotalsSumToTheWindowTotals()
    {
        // The invariant the balancing row exists for. Both a null account and a Guid pointing at nothing
        // are reachable in production — a restored pre-migration backup, or a half-finished delete.
        var card = Acc("Card");
        var cash = Acc("Cash");
        var rows = new[]
        {
            Spend(100m, Today, accountId: card.Id),
            Earn(500m, Today, accountId: cash.Id),
            Spend(30m, Today, accountId: null),            // never assigned
            Spend(70m, Today, accountId: Guid.NewGuid())   // points at an account that is gone
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, cash }, rows, null, Start(30), Today);

        breakdown.Sum(r => r.Income).Should().Be(500m);
        breakdown.Sum(r => r.Expense).Should().Be(200m);

        var unassigned = breakdown.Single(r => r.IsUnassigned);
        unassigned.Expense.Should().Be(100m, "both the null and the orphan row land here");
    }

    [Fact]
    public void ComputeAccountBreakdown_TransferOnTheLastDayWithATimeOfDay_IsInsideTheWindow()
    {
        // Transfer.Date has no truncating setter, unlike FinanceTransaction.Date. Compared without .Date
        // against a midnight bound, every transfer made today disappears — on the very day the user is
        // most likely to be looking at this screen.
        var card = Acc("Card");
        var cash = Acc("Cash");
        var transfers = new[]
        {
            new Transfer { FromAccountId = card.Id, ToAccountId = cash.Id, Amount = 200m, Date = Today.AddHours(23).AddMinutes(59) },
            new Transfer { FromAccountId = card.Id, ToAccountId = cash.Id, Amount = 50m, Date = Start(30).AddSeconds(1) }
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, cash }, Array.Empty<FinanceTransaction>(), transfers, Start(30), Today);

        breakdown.Single(r => r.AccountId == card.Id).TransferOut.Should().Be(250m);
        breakdown.Single(r => r.AccountId == cash.Id).TransferIn.Should().Be(250m);
    }

    [Fact]
    public void ComputeAccountBreakdown_TransfersStayOutOfIncomeAndExpense()
    {
        var card = Acc("Card");
        var cash = Acc("Cash");
        var transfers = new[]
        {
            new Transfer { FromAccountId = card.Id, ToAccountId = cash.Id, Amount = 200m, Date = Today }
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, cash }, Array.Empty<FinanceTransaction>(), transfers, Start(30), Today);

        breakdown.Should().OnlyContain(r => r.Income == 0m && r.Expense == 0m);
        breakdown.Single(r => r.AccountId == card.Id).TransferNet.Should().Be(-200m);
        breakdown.Single(r => r.AccountId == cash.Id).TransferNet.Should().Be(200m);
    }

    [Fact]
    public void ComputeAccountBreakdown_TransfersNetToZeroAcrossEveryAccount()
    {
        var card = Acc("Card");
        var cash = Acc("Cash");
        var transfers = new[]
        {
            new Transfer { FromAccountId = card.Id, ToAccountId = cash.Id, Amount = 200m, Date = Today },
            new Transfer { FromAccountId = cash.Id, ToAccountId = card.Id, Amount = 75m, Date = Today }
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, cash }, Array.Empty<FinanceTransaction>(), transfers, Start(30), Today);

        breakdown.Sum(r => r.TransferNet).Should().Be(0m);
    }

    [Fact]
    public void ComputeAccountBreakdown_ATransferLegOnAMissingAccount_LandsInTheBalancingRow()
    {
        // Guards the limitation the doc comment describes: money really did leave the known account, so
        // the known side must still count it, and the unknown side must not simply disappear.
        var card = Acc("Card");
        var transfers = new[]
        {
            new Transfer { FromAccountId = card.Id, ToAccountId = Guid.NewGuid(), Amount = 200m, Date = Today }
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card }, Array.Empty<FinanceTransaction>(), transfers, Start(30), Today);

        breakdown.Single(r => r.AccountId == card.Id).TransferOut.Should().Be(200m);
        breakdown.Single(r => r.IsUnassigned).TransferIn.Should().Be(200m);
        breakdown.Sum(r => r.TransferNet).Should().Be(0m);
    }

    [Fact]
    public void ComputeAccountBreakdown_ActiveAccountWithNoActivity_IsKeptAtZero()
    {
        var card = Acc("Card");
        var unused = Acc("Unused");

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, unused }, new[] { Spend(100m, Today, accountId: card.Id) }, null, Start(30), Today);

        // A zero is a real answer to "which account did I spend from", and a stable row set stops the
        // card reshuffling every time the period changes.
        breakdown.Should().HaveCount(2);
        breakdown.Single(r => r.AccountId == unused.Id).HasActivity.Should().BeFalse();
    }

    [Fact]
    public void ComputeAccountBreakdown_ArchivedAccountAppearsOnlyWhenItHasActivity()
    {
        var card = Acc("Card");
        var quietArchive = Acc("Old", archived: true);
        var busyArchive = Acc("Older", archived: true);

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { card, quietArchive, busyArchive },
            new[] { Spend(40m, Today, accountId: busyArchive.Id) },
            null, Start(30), Today);

        breakdown.Should().NotContain(r => r.AccountId == quietArchive.Id);
        breakdown.Single(r => r.AccountId == busyArchive.Id).IsArchived.Should().BeTrue();
    }

    [Fact]
    public void ComputeAccountBreakdown_OrdersBySpendWithTheBalancingRowLast()
    {
        var small = Acc("Small");
        var big = Acc("Big");
        var rows = new[]
        {
            Spend(10m, Today, accountId: small.Id),
            Spend(900m, Today, accountId: big.Id),
            Spend(5m, Today, accountId: null)
        };

        var breakdown = FinanceReportCalculator.ComputeAccountBreakdown(
            new[] { small, big }, rows, null, Start(30), Today);

        breakdown.Select(r => r.AccountId).Should().Equal(big.Id, small.Id, null);
    }

    [Fact]
    public void ComputeAccountBreakdown_WithNoAccountsAndNoRows_IsEmpty()
    {
        FinanceReportCalculator.ComputeAccountBreakdown(
            null!, null!, null, Start(30), Today).Should().BeEmpty();
    }
}
