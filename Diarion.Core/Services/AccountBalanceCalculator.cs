using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Pure account-balance maths. An account's balance is its opening balance, plus the net of its own
/// transactions (income − expense), plus the net of transfers into and out of it; the aggregate ("All")
/// balance is the sum of every account's opening balance plus the net of all transactions. Deterministic,
/// no I/O.
/// </summary>
public static class AccountBalanceCalculator
{
    public static decimal ComputeBalance(
        Account account,
        IEnumerable<FinanceTransaction> transactions,
        IEnumerable<Transfer>? transfers = null)
    {
        if (account == null) return 0m;
        var own = (transactions ?? Enumerable.Empty<FinanceTransaction>())
            .Where(t => t.AccountId == account.Id);
        return account.InitialBalance + Net(own) + TransferNet(account.Id, transfers);
    }

    /// <summary>
    /// Transfers are absent by design: they only move money between the accounts being summed, so they
    /// net to zero. Pass every account, including archived ones — omitting one drops its opening balance
    /// while its transactions still count.
    /// </summary>
    public static decimal ComputeTotal(IEnumerable<Account> accounts, IEnumerable<FinanceTransaction> transactions)
    {
        var initial = (accounts ?? Enumerable.Empty<Account>()).Sum(a => a.InitialBalance);
        return initial + Net(transactions ?? Enumerable.Empty<FinanceTransaction>());
    }

    private static decimal TransferNet(Guid accountId, IEnumerable<Transfer>? transfers)
    {
        decimal net = 0m;
        foreach (var t in transfers ?? Enumerable.Empty<Transfer>())
        {
            if (t.FromAccountId == accountId) net -= t.Amount;
            if (t.ToAccountId == accountId) net += t.Amount;
        }
        return net;
    }

    private static decimal Net(IEnumerable<FinanceTransaction> transactions)
    {
        decimal income = 0m;
        decimal expense = 0m;
        foreach (var t in transactions)
        {
            if (t.Type == TransactionType.Income) income += t.Amount;
            else expense += t.Amount;
        }
        return income - expense;
    }
}
