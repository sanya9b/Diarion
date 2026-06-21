using System;

namespace Diarion.Models;

public enum TransactionType
{
    Income,
    Expense
}

public class FinanceTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    private DateTime _date = DateTime.Today;
    public DateTime Date 
    { 
        get => _date; 
        set => _date = value.Date; 
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
