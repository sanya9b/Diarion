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
    private ILiteCollection<RecurringTransaction> RecurringCollection => _dbContext.GetCollection<RecurringTransaction>(DatabaseConstants.RecurringTransactionsCollection);

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

            // Recurring rules point at an account too. Leave one behind and it keeps posting into a
            // deleted account: those rows vanish from every per-account balance while still counting in
            // the total, so "All" stops equalling the sum of the accounts with nothing reporting an error.
            foreach (var rule in RecurringCollection.FindAll().Where(r => r.AccountId == id).ToList())
            {
                rule.AccountId = reassignToId;
                RecurringCollection.Update(rule);
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

    public Task<List<RecurringTransaction>> GetRecurringTransactionsAsync()
    {
        return Task.Run(() => RecurringCollection.Query().OrderBy(r => r.CreatedAt).ToList());
    }

    public Task SaveRecurringTransactionAsync(RecurringTransaction rule)
    {
        return Task.Run(() =>
        {
            if (rule.CreatedAt == default)
            {
                rule.CreatedAt = DateTime.UtcNow;
            }
            RecurringCollection.Upsert(rule);
        });
    }

    public Task DeleteRecurringTransactionAsync(Guid id, bool deletePostedTransactions)
    {
        return Task.Run(() =>
        {
            if (deletePostedTransactions)
            {
                // In-memory filter: LiteDB's LINQ translation breaks on nullable Guid equality, as above.
                foreach (var tx in FinanceCollection.FindAll().Where(t => t.RecurringTransactionId == id).ToList())
                {
                    FinanceCollection.Delete(tx.Id);
                }
            }

            // Rows that stay keep their RecurringTransactionId pointing at the deleted rule rather than
            // being nulled: that preserves provenance and keeps the duplicate guard working if an
            // identical rule is created again.
            RecurringCollection.Delete(id);
        });
    }

    public Task<PostingResult> ApplyDuePostingsAsync(DateTime today, Guid? fallbackAccountId = null)
    {
        return Task.Run(() =>
        {
            var rules = RecurringCollection.Query().ToList();
            if (rules.Count == 0)
            {
                return new PostingResult();
            }

            var plan = RecurrencePostingPlanner.Plan(rules, FinanceCollection.FindAll(), today, fallbackAccountId);

            if (plan.ToPost.Count > 0)
            {
                FinanceCollection.InsertBulk(plan.ToPost);
            }

            foreach (var rule in rules)
            {
                if (!plan.Watermarks.TryGetValue(rule.Id, out var mark)) continue;
                if (rule.LastPostedThrough >= mark) continue;

                rule.LastPostedThrough = mark;
                RecurringCollection.Update(rule);
            }

            return new PostingResult { PostedCount = plan.ToPost.Count, Pending = plan.Pending };
        });
    }

    public Task ConfirmOccurrenceAsync(Guid ruleId, DateTime occurrence, Guid? fallbackAccountId = null)
    {
        return Task.Run(() =>
        {
            var rule = RecurringCollection.FindById(ruleId);
            if (rule == null) return;

            var day = occurrence.Date;
            var alreadyPosted = FinanceCollection.FindAll()
                .Any(t => t.RecurringTransactionId == ruleId && t.Date.Date == day);

            if (!alreadyPosted)
            {
                FinanceCollection.Insert(RecurrencePostingPlanner.Materialize(rule, day, fallbackAccountId));
            }

            AdvanceWatermark(rule, day);
        });
    }

    public Task SkipOccurrenceAsync(Guid ruleId, DateTime occurrence)
    {
        return Task.Run(() =>
        {
            var rule = RecurringCollection.FindById(ruleId);
            if (rule == null) return;

            AdvanceWatermark(rule, occurrence.Date);
        });
    }

    /// <summary>
    /// Only ever moves forward, so answering an older occurrence after a newer one cannot reopen days that
    /// were already dealt with. It does mean confirming out of order dismisses the earlier ones, which is
    /// why the pending list is shown oldest first.
    /// </summary>
    private void AdvanceWatermark(RecurringTransaction rule, DateTime day)
    {
        if (rule.LastPostedThrough >= day) return;

        rule.LastPostedThrough = day;
        RecurringCollection.Update(rule);
    }
}