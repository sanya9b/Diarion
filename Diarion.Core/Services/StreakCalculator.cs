using System;
using System.Collections.Generic;

namespace Diarion.Services;

/// <summary>
/// Counts consecutive journaled days with no forgiveness. Kept as the strict, zero-quota reading of
/// <see cref="StreakWalker"/> — callers that want grace days go through the walker directly.
/// Deterministic and pure — "today" is passed in.
/// </summary>
public static class StreakCalculator
{
    public static int Calculate(IEnumerable<DateTime> journaledDates, DateTime today)
        => StreakWalker.Walk(journaledDates, today, graceBudget: 0).Length;
}
