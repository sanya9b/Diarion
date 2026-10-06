using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>An occurrence that has come due on a rule the user asked to confirm by hand.</summary>
public sealed class PendingOccurrence
{
    public Guid RuleId { get; init; }
    public DateTime Date { get; init; }
    public RecurringTransaction Rule { get; init; } = new();
}

/// <summary>What the caller should write. Nothing here has touched the database yet.</summary>
public sealed class PostingPlan
{
    public List<FinanceTransaction> ToPost { get; init; } = new();
    public List<PendingOccurrence> Pending { get; init; } = new();

    /// <summary>Rules whose watermark should move, and the day it should move to.</summary>
    public Dictionary<Guid, DateTime> Watermarks { get; init; } = new();
}

/// <summary>
/// Works out which recurring rules have come due and what should happen to each. Pure and deterministic —
/// "today" is passed in and nothing is read or written — so the guards below can be tested directly.
/// </summary>
public static class RecurrencePostingPlanner
{
    /// <summary>
    /// How far back a single run will reach. Just over a year, so any monthly rule fires at least once
    /// after a long gap, without a stale watermark tipping thousands of rows into the ledger.
    /// A window clipped by this cap <b>permanently skips</b> the occurrences before it, because the
    /// watermark then jumps to today: bounded and silent beats unbounded and destructive, but it is a
    /// real trade rather than a detail.
    /// </summary>
    public const int MaxBackfillDays = 400;

    public static PostingPlan Plan(
        IEnumerable<RecurringTransaction> rules,
        IEnumerable<FinanceTransaction> existing,
        DateTime today,
        Guid? fallbackAccountId = null)
    {
        var plan = new PostingPlan();
        var day = today.Date;

        // Occurrences already on the books. Independent of the watermark on purpose: it is what survives a
        // restored backup, a hand-edited database, or a rule deleted with its rows kept and then recreated.
        var alreadyPosted = (existing ?? Enumerable.Empty<FinanceTransaction>())
            .Where(t => t.RecurringTransactionId != null)
            .Select(t => (t.RecurringTransactionId!.Value, t.Date.Date))
            .ToHashSet();

        foreach (var rule in rules ?? Enumerable.Empty<RecurringTransaction>())
        {
            if (rule == null || rule.IsPaused) continue;

            var windowStart = rule.LastPostedThrough.AddDays(1);
            var earliest = day.AddDays(-MaxBackfillDays);
            if (windowStart < earliest) windowStart = earliest;
            if (windowStart > day) continue;

            var occurrences = (rule.Recurrence ?? new RecurrenceRule())
                .Enumerate(windowStart, day)
                .ToList();

            if (rule.AutoPost)
            {
                foreach (var occurrence in occurrences)
                {
                    if (!alreadyPosted.Add((rule.Id, occurrence))) continue;
                    plan.ToPost.Add(Materialize(rule, occurrence, fallbackAccountId));
                }

                // Moves even when the window held nothing, which is what makes a second run in the same
                // session a genuine no-op rather than a re-scan.
                plan.Watermarks[rule.Id] = day;
            }
            else
            {
                // The watermark stays put: pending occurrences are computed, never stored, so advancing
                // past one the user has not answered would silently drop it. Confirm and skip move it.
                foreach (var occurrence in occurrences)
                {
                    if (alreadyPosted.Contains((rule.Id, occurrence))) continue;
                    plan.Pending.Add(new PendingOccurrence
                    {
                        RuleId = rule.Id,
                        Date = occurrence,
                        Rule = rule
                    });
                }
            }
        }

        plan.Pending.Sort((left, right) => left.Date.CompareTo(right.Date));
        return plan;
    }

    /// <summary>Builds the row a due occurrence turns into.</summary>
    public static FinanceTransaction Materialize(
        RecurringTransaction rule,
        DateTime occurrence,
        Guid? fallbackAccountId = null)
        => new()
        {
            Type = rule.Type,
            AccountId = rule.AccountId ?? fallbackAccountId,
            Amount = rule.Amount,
            Category = rule.Category,
            Note = rule.Note,
            Date = occurrence,
            RecurringTransactionId = rule.Id
        };
}
