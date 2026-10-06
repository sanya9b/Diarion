using System;

namespace Diarion.Models;

/// <summary>
/// A movement of money between two accounts. Deliberately not a <see cref="FinanceTransaction"/>: a
/// transfer is neither income nor expense, and modelling it as one would silently inflate month totals,
/// budgets, category statistics and the CSV export, all of which branch on <see cref="TransactionType"/>.
/// </summary>
public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromAccountId { get; set; }

    public Guid ToAccountId { get; set; }

    public decimal Amount { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Today;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
