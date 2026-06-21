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

    public Task<List<FinanceTransaction>> GetFinanceTransactionsAsync()
    {
        return Task.Run(() => FinanceCollection.Query().OrderByDescending(x => x.Date).ToList());
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
}