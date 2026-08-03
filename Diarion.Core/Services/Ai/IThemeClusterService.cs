using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <param name="Label">The leading sentence of the passage most typical of the theme, verbatim.</param>
/// <param name="DayCount">Distinct days the theme appears on — the honest measure of how present it was.</param>
/// <param name="FirstSeen">Earliest day in the cluster.</param>
/// <param name="LastSeen">Latest day in the cluster.</param>
public sealed record DiaryTheme(string Label, int DayCount, DateTime FirstSeen, DateTime LastSeen);

/// <summary>
/// Groups a period's writing into recurring themes.
/// </summary>
/// <remarks>
/// Labels are quoted, not generated: the theme is named by the sentence closest to its own centre,
/// so this works on the encoder alone. Counting distinct days rather than passages is deliberate —
/// one long evening spent writing about the same thing is one day of that theme, not eight.
/// </remarks>
public interface IThemeClusterService
{
    Task<IReadOnlyList<DiaryTheme>> ClusterAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default);
}
