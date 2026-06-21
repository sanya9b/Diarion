using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IFinanceService
{
    Task<List<FinanceTransaction>> GetFinanceTransactionsAsync();
    Task SaveFinanceTransactionAsync(FinanceTransaction transaction);
    Task DeleteFinanceTransactionAsync(Guid id);
}