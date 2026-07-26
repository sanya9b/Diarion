using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IFinanceService
{
    Task<List<FinanceTransaction>> GetFinanceTransactionsAsync();
    Task<List<FinanceTransaction>> GetFinanceTransactionsForStatsAsync(DateTime startDate, DateTime endDate);
    Task<List<string>> GetCategoriesAsync(TransactionType type);
    Task SaveFinanceTransactionAsync(FinanceTransaction transaction);
    Task DeleteFinanceTransactionAsync(Guid id);

    Task<List<Budget>> GetBudgetsAsync();
    Task SaveBudgetAsync(Budget budget);
    Task DeleteBudgetAsync(Guid id);

    /// <summary>
    /// Archived accounts still own transactions and an opening balance, so callers computing totals
    /// must pass <c>true</c>; only the account strip filters them out.
    /// </summary>
    Task<List<Account>> GetAccountsAsync(bool includeArchived = false);
    Task SaveAccountAsync(Account account);
    /// <summary>
    /// Deletes <paramref name="id"/> after moving its transactions and transfers to
    /// <paramref name="reassignToId"/>. Transfers whose two legs collapse onto the same account are dropped.
    /// </summary>
    Task DeleteAccountAsync(Guid id, Guid reassignToId);

    Task<List<Transfer>> GetTransfersAsync();
    Task SaveTransferAsync(Transfer transfer);
    Task DeleteTransferAsync(Guid id);
}