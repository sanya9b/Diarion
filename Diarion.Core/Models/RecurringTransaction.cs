using System;

namespace Diarion.Models;

/// <summary>
/// A rule that materializes <see cref="FinanceTransaction"/> rows on a schedule. Deliberately a rule and
/// not a transaction with a "planned" flag: a posted row is then indistinguishable from a hand-entered one,
/// so budgets, balances, statistics and the CSV export need no notion of an unrealized row — the same
/// reasoning that kept <see cref="Transfer"/> out of <see cref="TransactionType"/>.
/// </summary>
public class RecurringTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TransactionType Type { get; set; }

    /// <summary>Account to post into. Null falls back to the default account at posting time.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Always positive; the sign comes from <see cref="Type"/>, as on a transaction.</summary>
    public decimal Amount { get; set; }

    public string Category { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    public RecurrenceRule Recurrence { get; set; } = new();

    /// <summary>Post occurrences without asking. False routes them to confirmation instead.</summary>
    public bool AutoPost { get; set; } = true;

    /// <summary>Stops the rule producing anything without deleting it or its history.</summary>
    public bool IsPaused { get; set; }

    private DateTime _lastPostedThrough;

    /// <summary>
    /// Watermark: every occurrence on or before this day has been <b>dealt with</b> — posted, confirmed or
    /// explicitly skipped. Not "posted through": a rule that awaits confirmation must not advance past an
    /// occurrence the user has not answered, because pending occurrences are computed rather than stored
    /// and moving the mark would make them silently disappear.
    /// </summary>
    public DateTime LastPostedThrough
    {
        get => _lastPostedThrough;
        set => _lastPostedThrough = DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
