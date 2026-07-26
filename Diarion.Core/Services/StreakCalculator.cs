using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Services;

/// <summary>
/// Counts consecutive journaled days ending today or yesterday. Yesterday still counts so the streak
/// does not appear broken before the user has had a chance to write today. Deterministic and pure —
/// "today" is passed in.
/// </summary>
public static class StreakCalculator
{
    public static int Calculate(IEnumerable<DateTime> journaledDates, DateTime today)
    {
        var todayOnly = today.Date;

        // Future-dated rows are ignored rather than treated as the head of the chain: the debug seeder
        // writes entries days ahead, and a stray future date must not zero out a real streak.
        var dates = (journaledDates ?? Enumerable.Empty<DateTime>())
            .Select(d => d.Date)
            .Where(d => d <= todayOnly)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        if (dates.Count == 0) return 0;

        var newest = dates[0];
        if (newest != todayOnly && newest != todayOnly.AddDays(-1)) return 0;

        var expected = newest;
        var streak = 0;
        foreach (var date in dates)
        {
            if (date != expected) break;
            streak++;
            expected = expected.AddDays(-1);
        }

        return streak;
    }
}
