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
    /// <summary>Owning account. Nullable so legacy rows (pre-accounts) deserialize to null; the M003
    /// migration backfills a default account for existing transactions.</summary>
    public Guid? AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    private DateTime _date = DateTime.Today;
    public DateTime Date
    {
        get => _date;
        set => _date = value.Date;
    }

    /// <summary>The recurring rule that materialized this row, or null for a hand-entered one. Nullable so
    /// pre-Phase-C rows deserialize to null without a migration, as with <see cref="AccountId"/>. Together
    /// with <see cref="Date"/> it also identifies an occurrence, which is what stops a rule posting the
    /// same day twice after a restored backup.</summary>
    public Guid? RecurringTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
