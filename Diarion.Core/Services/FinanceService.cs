using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class FinanceService : IFinanceService
{
    private readonly IDatabaseContext _dbContext;

    public FinanceService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<FinanceTransaction> FinanceCollection => _dbContext.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
    private ILiteCollection<Budget> BudgetsCollection => _dbContext.GetCollection<Budget>(DatabaseConstants.BudgetsCollection);
    private ILiteCollection<Account> AccountsCollection => _dbContext.GetCollection<Account>(DatabaseConstants.AccountsCollection);
    private ILiteCollection<Transfer> TransfersCollection => _dbContext.GetCollection<Transfer>(DatabaseConstants.TransfersCollection);

    public Task<List<FinanceTransaction>> GetFinanceTransactionsAsync()
    {
        return Task.Run(() => FinanceCollection.Query().OrderByDescending(x => x.Date).ToList());
    }

    public Task<List<FinanceTransaction>> GetFinanceTransactionsForStatsAsync(DateTime startDate, DateTime endDate)
    {
        return Task.Run(() => FinanceCollection.Query()
            .Where(x => x.Date >= startDate.Date && x.Date <= endDate.Date)
            .OrderByDescending(x => x.Date)
            .ToList());
    }

    public Task<List<string>> GetCategoriesAsync(TransactionType type)
    {
        return Task.Run(() => 
        {
            return FinanceCollection.Query()
                .Where(x => x.Type == type)
                .Select(x => x.Category)
                .ToEnumerable()
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public Task SaveFinanceTransactionAsync(FinanceTransaction transaction)
    {
        return Task.Run(() =>
        {
            if (transaction.CreatedAt == default)
            {
                transaction.CreatedAt = DateTime.UtcNow;
            }
            FinanceCollection.Upsert(transaction);
        });
    }

    public Task DeleteFinanceTransactionAsync(Guid id)
    {
        return Task.Run(() =>
        {
            FinanceCollection.Delete(id);
        });
    }

    public Task<List<Budget>> GetBudgetsAsync()
    {
        return Task.Run(() => BudgetsCollection.Query().OrderBy(x => x.Category).ToList());
    }

    public Task SaveBudgetAsync(Budget budget)
    {
        return Task.Run(() =>
        {
            if (budget.CreatedAt == default)
            {
                budget.CreatedAt = DateTime.UtcNow;
            }
            BudgetsCollection.Upsert(budget);
        });
    }

    public Task DeleteBudgetAsync(Guid id)
    {
        return Task.Run(() => BudgetsCollection.Delete(id));
    }

    public Task<List<Account>> GetAccountsAsync(bool includeArchived = false)
    {
        return Task.Run(() =>
        {
            var all = AccountsCollection.Query().OrderBy(a => a.CreatedAt).ToList();
            return includeArchived ? all : all.Where(a => !a.IsArchived).ToList();
        });
    }

    public Task SaveAccountAsync(Account account)
    {
        return Task.Run(() =>
        {
            if (account.CreatedAt == default)
            {
                account.CreatedAt = DateTime.UtcNow;
            }
            AccountsCollection.Upsert(account);
        });
    }

    public Task DeleteAccountAsync(Guid id, Guid reassignToId)
    {
        return Task.Run(() =>
        {
            // Move the account's transactions to the fallback account before removing it, so no
            // transaction is left orphaned (in-memory filter — nullable Guid equality in LiteDB LINQ).
            var toReassign = FinanceCollection.FindAll().Where(t => t.AccountId == id).ToList();
            foreach (var tx in toReassign)
            {
                tx.AccountId = reassignToId;
                FinanceCollection.Update(tx);
            }

            foreach (var transfer in TransfersCollection.FindAll().ToList())
            {
                if (transfer.FromAccountId != id && transfer.ToAccountId != id) continue;

                if (transfer.FromAccountId == id) transfer.FromAccountId = reassignToId;
                if (transfer.ToAccountId == id) transfer.ToAccountId = reassignToId;

                // Both legs collapsed onto the same account — the transfer no longer moves anything.
                if (transfer.FromAccountId == transfer.ToAccountId)
                {
                    TransfersCollection.Delete(transfer.Id);
                }
                else
                {
                    TransfersCollection.Update(transfer);
                }
            }

            AccountsCollection.Delete(id);
        });
    }

    public Task<List<Transfer>> GetTransfersAsync()
    {
        return Task.Run(() => TransfersCollection.Query().OrderByDescending(t => t.Date).ToList());
    }

    public Task SaveTransferAsync(Transfer transfer)
    {
        return Task.Run(() => TransfersCollection.Upsert(transfer));
    }

    public Task DeleteTransferAsync(Guid id)
    {
        return Task.Run(() => TransfersCollection.Delete(id));
    }
}