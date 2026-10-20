namespace Diarion.Services.Store;

/// <summary>
/// What this install looked like at process start, captured before onboarding can complete in the
/// current launch. It is the offline half of the legacy rule (spec 15, R2.1): a user who had
/// finished onboarding before the first paid build ran was a 1.0 user.
/// </summary>
public interface IInstallEvidence
{
    /// <summary>True exactly once per install: the first launch of a build that carries the paywall.
    /// A fresh install of a paid build is also its first paid launch — but onboarding cannot have
    /// been completed yet, so the rule stays false for it.</summary>
    bool IsFirstLaunchOfPaidEra { get; }

    /// <summary>Whether onboarding had been completed by a previous launch, read before this launch
    /// could change it.</summary>
    bool OnboardingCompletedBeforeThisLaunch { get; }
}
