using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Store;

namespace Diarion.Services.InsightPath;

/// <inheritdoc />
/// <remarks>
/// The visibility rule, spelled out because the acceptance criteria refine the spec's one-line
/// formula in ways the formula alone does not say:
/// <list type="bullet">
/// <item>Read-only: never. The path cannot move, so there is nothing to show (AC6).</item>
/// <item>Short of the last milestone with no holding hypothesis: shown — the card is the path
/// (AC1–AC3, AC7).</item>
/// <item>At the last milestone with a holding hypothesis: shown, with the hypothesis, until the
/// user dismisses it; then never again, because there is no next milestone (AC4).</item>
/// <item>At the last milestone with nothing holding: hidden. The statistics screen already says
/// "no hypothesis holds", and a congratulation here would contradict it (AC5).</item>
/// <item>A holding hypothesis for a legacy user: never. Someone who had the finding months before
/// this card existed is not arriving anywhere, and "your first hypothesis" would be untrue
/// (AC6; spec 17, risks). A legacy user without one walks the path like anyone else.</item>
/// <item>A holding hypothesis short of the last milestone — a text theme can clear the bar while
/// no logged factor has — hides the card too: someone with a finding is not on the path.</item>
/// <item>Whatever the above says, nothing below the milestone the user hid at (D5).</item>
/// </list>
/// </remarks>
public sealed class InsightPathService : IInsightPathService
{
    /// <summary>
    /// The window, fixed rather than borrowed from the statistics chips (spec 17, D1). Stable from
    /// one day to the next — "since the first of the month" would restart the path monthly — and
    /// the window the statistics screen is sent to from the card, so the hypothesis shown here is
    /// the one found there. Thirty days hold fourteen paired days with two misses a week.
    /// </summary>
    public const int WindowDays = 30;

    private readonly ICorrelationService _correlations;
    private readonly IProfileService _profiles;
    private readonly IEntitlementService _entitlements;

    public InsightPathService(
        ICorrelationService correlations,
        IProfileService profiles,
        IEntitlementService entitlements)
    {
        _correlations = correlations;
        _profiles = profiles;
        _entitlements = entitlements;
    }

    private static StatsRange Window => StatsRange.LastDays(WindowDays);

    public async Task<InsightPathState> GetAsync()
    {
        var access = _entitlements.Current;
        var window = Window;

        // Themes stay out of the count: whether today counted must not wait on the encoder. They
        // stay in the hypothesis check, which is the pass the statistics screen runs, so a finding
        // that holds there holds here — and one that holds there is enough to end the path here.
        var readiness = await _correlations.GetReadinessAsync(window, includeThemes: false);
        var holding = (await _correlations.GetMoodCorrelationsAsync(window)).Where(c => c.Holds).ToList();
        var profile = await _profiles.GetUserProfileAsync();

        var reached = InsightPathMilestones.Reached(readiness.PairedDays);
        var atLast = reached == InsightPathMilestones.Last;
        var hasHypothesis = holding.Count > 0;

        var walking = !hasHypothesis && !atLast;
        var arrived = hasHypothesis && atLast && access.Source != AccessSource.Legacy;
        var isVisible = access.CanWrite
            && profile.IsInsightPathEnabled
            && reached >= profile.InsightPathHiddenBelowMilestone
            && (walking || arrived);

        return new InsightPathState(
            isVisible,
            readiness.PairedDays,
            readiness.RequiredDays,
            readiness.BestFactorKey,
            reached,
            InsightPathMilestones.Next(readiness.PairedDays),
            atLast ? Strongest(holding) : null);
    }

    public async Task HideUntilNextMilestoneAsync()
    {
        // Readiness alone: the next milestone depends on the count, not on whether anything holds,
        // and this is a button press — no reason to run the correlation pass a second time.
        var readiness = await _correlations.GetReadinessAsync(Window, includeThemes: false);
        var profile = await _profiles.GetUserProfileAsync();

        profile.InsightPathHiddenBelowMilestone = InsightPathMilestones.Next(readiness.PairedDays) ?? int.MaxValue;
        await _profiles.SaveUserProfileAsync(profile);
    }

    /// <summary>
    /// Confidence first, then size of effect: the dots are what the user is shown as evidence, and
    /// between two findings with the same dots the larger coefficient is the one worth leading with.
    /// </summary>
    private static MoodCorrelation? Strongest(List<MoodCorrelation> holding) =>
        holding
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => Math.Abs(c.Coefficient))
            .FirstOrDefault();
}
