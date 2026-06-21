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
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
