using System;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Pure quit-tracker maths: how long the user has been clean (reset by the latest relapse), the money
/// saved, and milestone progress. Deterministic and day-granular; the live HH:MM:SS ticker is formatted
/// in the ViewModel from <see cref="CleanSince"/>.
/// </summary>
public static class QuitTrackerCalculator
{
    public static readonly int[] MilestoneDays = { 1, 3, 7, 14, 30, 60, 90, 180, 365 };

    /// <summary>The moment the current clean streak began: the latest relapse (if any, and not before the
    /// start), otherwise the tracker's start date. Never in the future.</summary>
    public static DateTime CleanSince(HarmfulHabitTracker tracker, DateTime today)
    {
        var since = tracker.StartDate.Date;

        if (tracker.Relapses is { Count: > 0 })
        {
            var lastRelapse = tracker.Relapses.Max(r => r.Date.Date);
            if (lastRelapse > since) since = lastRelapse;
        }

        if (since > today.Date) since = today.Date;
        return since;
    }

    public static int CleanDays(HarmfulHabitTracker tracker, DateTime today)
    {
        var days = (today.Date - CleanSince(tracker, today)).Days;
        return days < 0 ? 0 : days;
    }

    public static decimal MoneySaved(HarmfulHabitTracker tracker, DateTime today)
    {
        if (tracker.CostPerUnit <= 0 || tracker.UnitsPerDay <= 0) return 0m;
        return CleanDays(tracker, today) * (decimal)tracker.UnitsPerDay * tracker.CostPerUnit;
    }

    public static int AchievedCount(int cleanDays) => MilestoneDays.Count(m => cleanDays >= m);

    /// <summary>The next milestone (in days) not yet reached, or null once all are achieved.</summary>
    public static int? NextMilestone(int cleanDays)
    {
        foreach (var m in MilestoneDays)
        {
            if (cleanDays < m) return m;
        }
        return null;
    }
}
