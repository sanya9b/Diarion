using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Services;

/// <summary>The outcome of a streak walk: how long the run is, and how much forgiveness it leaned on.</summary>
public readonly record struct StreakResult(int Length, int GraceUsed)
{
    public static readonly StreakResult None = new(0, 0);

    /// <summary>True when the run only survives because missed days were forgiven.</summary>
    public bool HeldByGrace => Length > 0 && GraceUsed > 0;
}

/// <summary>
/// The one walker behind every consecutive-day streak in the app — the diary's daily chain and a habit's
/// scheduled chain are the same walk with a different notion of which days count.
///
/// A run may forgive up to <c>graceBudget</c> missed days in total; the next miss after the budget runs out
/// ends it. The budget is per run, not per gap, so journaling every other day burns through it quickly
/// instead of yielding an endless streak. Nothing is persisted — the answer is a pure function of the dates,
/// so changing the quota re-reads history rather than rewriting it.
/// </summary>
public static class StreakWalker
{
    // A degenerate schedule (one that matches no day at all) must not spin forever.
    private const int MaxLookbackDays = 3660;

    public static StreakResult Walk(
        IEnumerable<DateTime> completedDates,
        DateTime today,
        int graceBudget,
        Func<DateTime, bool>? isScheduled = null)
    {
        var todayOnly = today.Date;
        var grace = Math.Max(0, graceBudget);
        var scheduled = isScheduled ?? (_ => true);

        // Future-dated rows are ignored rather than treated as the head of the chain: the debug seeder
        // writes entries days ahead, and a stray future date must not zero out a real streak.
        var done = (completedDates ?? Enumerable.Empty<DateTime>())
            .Select(d => d.Date)
            .Where(d => d <= todayOnly)
            .ToHashSet();

        if (done.Count == 0) return StreakResult.None;

        var oldest = done.Min();

        var anchor = MostRecentScheduledOnOrBefore(todayOnly, scheduled);
        if (anchor == null) return StreakResult.None;
        var cursor = anchor.Value;

        // Today is still in progress, so an unwritten today is not a miss and must not cost quota.
        if (cursor == todayOnly && !done.Contains(cursor))
        {
            var previous = MostRecentScheduledOnOrBefore(todayOnly.AddDays(-1), scheduled);
            if (previous == null) return StreakResult.None;
            cursor = previous.Value;
        }

        int length = 0;
        int used = 0;
        int usedAtLastHit = 0;

        for (int guard = 0; guard < MaxLookbackDays && cursor >= oldest; guard++)
        {
            if (!scheduled(cursor))
            {
                // Not a day this habit was ever due — invisible to both the count and the quota.
                cursor = cursor.AddDays(-1);
                continue;
            }

            if (done.Contains(cursor))
            {
                length++;
                usedAtLastHit = used;
            }
            else if (used == grace)
            {
                break;
            }
            else
            {
                used++;
            }

            cursor = cursor.AddDays(-1);
        }

        // Report the quota spent up to the last day actually logged. Grace burned after that point held
        // nothing up, and counting it would overstate how close the run is to breaking.
        return length == 0 ? StreakResult.None : new StreakResult(length, usedAtLastHit);
    }

    /// <summary>Walks back from <paramref name="day"/> to the nearest scheduled day, or null if there is none.</summary>
    public static DateTime? MostRecentScheduledOnOrBefore(DateTime day, Func<DateTime, bool> isScheduled)
    {
        var cursor = day.Date;
        for (int i = 0; i < MaxLookbackDays; i++)
        {
            if (isScheduled(cursor)) return cursor;
            cursor = cursor.AddDays(-1);
        }

        return null;
    }
}
