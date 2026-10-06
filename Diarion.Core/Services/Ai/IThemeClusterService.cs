using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <param name="Label">The leading sentence of the passage most typical of the theme, verbatim.</param>
/// <param name="Days">
/// Distinct days the theme appears on, ascending — the honest measure of how present it was, and the
/// series the correlation pass reads. Never empty.
/// </param>
public sealed record DiaryTheme(string Label, IReadOnlyList<DateTime> Days)
{
    /// <summary>How many days the theme appears on.</summary>
    public int DayCount => Days.Count;

    /// <summary>Earliest day in the cluster.</summary>
    public DateTime FirstSeen => Days[0];

    /// <summary>Latest day in the cluster.</summary>
    public DateTime LastSeen => Days[^1];
}

/// <param name="Themes">Recurring themes, densest first.</param>
/// <param name="IndexedDays">
/// Distinct days in the window with at least one indexed diary passage, ascending. This is the
/// population the themes were drawn from, and the only days on which "the theme was absent" is an
/// observation rather than a gap: a day with no entry is not a day without the theme, and neither is
/// a day the indexer has not reached yet. Includes days whose passages were all too short to be
/// themed — the user wrote, and this theme is not what they wrote about.
/// </param>
public sealed record ThemeSummary(IReadOnlyList<DiaryTheme> Themes, IReadOnlyList<DateTime> IndexedDays);

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

    /// <summary>
    /// The themes plus the days they were drawn from. Correlating a theme against mood needs both:
    /// the days it appeared on mean nothing without the days it could have appeared on and did not.
    /// </summary>
    Task<ThemeSummary> SummariseAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stand-in used when the AI module is not part of the picture — tests that construct a consumer
/// directly, and any host that has not registered the real service. Mirrors
/// <see cref="NullTextEmbedder"/>, except that no theme is a legitimate answer rather than an error:
/// the real service returns exactly this when AI is switched off.
/// </summary>
public sealed class NullThemeClusterService : IThemeClusterService
{
    public Task<IReadOnlyList<DiaryTheme>> ClusterAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DiaryTheme>>([]);

    public Task<ThemeSummary> SummariseAsync(
        DateTime start,
        DateTime end,
        int maxThemes = 5,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ThemeSummary([], []));
}
