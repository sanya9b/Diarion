using System;
using Diarion.Models;

namespace Diarion.ViewModels;

/// <summary>
/// One row of the unified finance feed. Transactions, transfers and occurrences awaiting confirmation all
/// land in a single date-sorted list, which is why they need a common base: the page's CollectionView can
/// only sort and virtualize one collection, and the three kinds carry different fields.
/// </summary>
public abstract class FinanceFeedItem
{
    public Guid Id { get; init; }

    /// <summary>Always truncated to a calendar day by the builder — <see cref="Transfer.Date"/> is not.</summary>
    public DateTime Date { get; init; }

    /// <summary>Tiebreaker within a day, so two rows on the same date keep a stable order.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Rows that need an answer float above settled ones on the same day.</summary>
    public virtual int SortRank => 0;
}

/// <summary>A posted or hand-entered transaction. Keeps the raw model so the template binds it directly.</summary>
public class TransactionFeedItem : FinanceFeedItem
{
    public FinanceTransaction Model { get; init; } = new();

    /// <summary>Materialized by a recurring rule rather than typed in.</summary>
    public bool IsFromPlan { get; init; }
}

/// <summary>A transfer between accounts, pre-formatted because it shares no fields with a transaction.</summary>
public class TransferFeedItem : FinanceFeedItem
{
    public string FromName { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
    public string AmountText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

/// <summary>An occurrence that has come due on a rule the user chose to confirm by hand.</summary>
public class PlannedFeedItem : FinanceFeedItem
{
    public Guid RuleId { get; init; }
    public string Category { get; init; } = string.Empty;
    public string AmountText { get; init; } = string.Empty;
    public string RecurrenceText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public bool IsExpense { get; init; }

    public override int SortRank => 2;
}
