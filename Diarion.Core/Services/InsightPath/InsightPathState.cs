using Diarion.Models;

namespace Diarion.Services.InsightPath;

/// <summary>
/// Everything the home-screen card needs, computed once per change (spec 17, R1.3).
/// </summary>
/// <param name="IsVisible">
/// Whether the card belongs on the screen at all: not in read-only mode, not for anyone whose
/// hypothesis already holds — unless they have just arrived at the last milestone and not yet
/// dismissed it — and not below the milestone the user chose to wait for.
/// </param>
/// <param name="PairedDays">Days in the window on which <paramref name="BestFactorKey"/> and a mood were both recorded.</param>
/// <param name="RequiredDays">The engine's bar, repeated here so the card never hardcodes fourteen.</param>
/// <param name="BestFactorKey">The factor nearest the bar, which the hint names; null before anything has paired.</param>
/// <param name="ReachedMilestone">The highest of <see cref="InsightPathMilestones.All"/> reached, or 0.</param>
/// <param name="NextMilestone">The next one ahead, or null after the last.</param>
/// <param name="FirstHypothesis">
/// The strongest correlation that holds, present only at the last milestone. It is the one the
/// statistics screen would list first, so the two never disagree about what the first finding was.
/// </param>
public sealed record InsightPathState(
    bool IsVisible,
    int PairedDays,
    int RequiredDays,
    string? BestFactorKey,
    int ReachedMilestone,
    int? NextMilestone,
    MoodCorrelation? FirstHypothesis);
