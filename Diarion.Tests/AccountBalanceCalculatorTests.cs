using System;
using System.Collections.Generic;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class AccountBalanceCalculatorTests
{
    private static FinanceTransaction Tx(TransactionType type, decimal amount, Guid? accountId) =>
        new() { Type = type, Amount = amount, AccountId = accountId, Date = new DateTime(2026, 7, 1) };

    [Fact]
    public void ComputeBalance_NoTransactions_IsOpeningBalance()
    {
        var account = new Account { InitialBalance = 250m };

        AccountBalanceCalculator.ComputeBalance(account, new List<FinanceTransaction>())
            .Should().Be(250m);
    }

    [Fact]
    public void ComputeBalance_NetsIncomeAgainstExpense_AndIgnoresOtherAccounts()
    {
        var mine = new Account { InitialBalance = 100m };
        var other = new Account();
        var tx = new List<FinanceTransaction>
        {
            Tx(TransactionType.Income, 60m, mine.Id),
            Tx(TransactionType.Expense, 25m, mine.Id),
            Tx(TransactionType.Expense, 999m, other.Id),
            Tx(TransactionType.Income, 999m, null)
        };

        AccountBalanceCalculator.ComputeBalance(mine, tx).Should().Be(135m);
    }

    [Fact]
    public void ComputeBalance_NullAccount_IsZero()
    {
        AccountBalanceCalculator.ComputeBalance(null!, new List<FinanceTransaction>()).Should().Be(0m);
    }

    [Fact]
    public void ComputeBalance_TransferDebitsSource_AndCreditsTarget()
    {
        var from = new Account { InitialBalance = 500m };
        var to = new Account { InitialBalance = 0m };
        var transfers = new List<Transfer>
        {
            new() { FromAccountId = from.Id, ToAccountId = to.Id, Amount = 200m }
        };

        AccountBalanceCalculator.ComputeBalance(from, new List<FinanceTransaction>(), transfers).Should().Be(300m);
        AccountBalanceCalculator.ComputeBalance(to, new List<FinanceTransaction>(), transfers).Should().Be(200m);
    }

    [Fact]
    public void ComputeBalance_TransferForAnotherPairOfAccounts_IsIgnored()
    {
        var mine = new Account { InitialBalance = 50m };
        var transfers = new List<Transfer>
        {
            new() { FromAccountId = Guid.NewGuid(), ToAccountId = Guid.NewGuid(), Amount = 400m }
        };

        AccountBalanceCalculator.ComputeBalance(mine, new List<FinanceTransaction>(), transfers).Should().Be(50m);
    }

    [Fact]
    public void ComputeTotal_SumsOpeningBalancesAndAllTransactions()
    {
        var a = new Account { InitialBalance = 100m };
        var b = new Account { InitialBalance = 40m };
        var tx = new List<FinanceTransaction>
        {
            Tx(TransactionType.Income, 30m, a.Id),
            Tx(TransactionType.Expense, 10m, b.Id)
        };

        AccountBalanceCalculator.ComputeTotal(new[] { a, b }, tx).Should().Be(160m);
    }

    [Fact]
    public void ComputeTotal_EqualsSumOfPerAccountBalances_EvenWithTransfers()
    {
        var a = new Account { InitialBalance = 100m };
        var b = new Account { InitialBalance = 40m };
        var tx = new List<FinanceTransaction> { Tx(TransactionType.Income, 30m, a.Id) };
        var transfers = new List<Transfer>
        {
            new() { FromAccountId = a.Id, ToAccountId = b.Id, Amount = 25m }
        };

        var perAccount = AccountBalanceCalculator.ComputeBalance(a, tx, transfers)
                       + AccountBalanceCalculator.ComputeBalance(b, tx, transfers);

        AccountBalanceCalculator.ComputeTotal(new[] { a, b }, tx).Should().Be(perAccount);
    }

    [Fact]
    public void ComputeTotal_OmittingAnArchivedAccount_LosesItsOpeningBalance()
    {
        var active = new Account { InitialBalance = 100m };
        var archived = new Account { InitialBalance = 60m, IsArchived = true };
        var tx = new List<FinanceTransaction> { Tx(TransactionType.Expense, 10m, archived.Id) };

        // Guards the bug this method's doc-comment warns about: the archived account's transaction
        // still counts, so dropping it from the list understates the total.
        AccountBalanceCalculator.ComputeTotal(new[] { active, archived }, tx).Should().Be(150m);
        AccountBalanceCalculator.ComputeTotal(new[] { active }, tx).Should().Be(90m);
    }
}
