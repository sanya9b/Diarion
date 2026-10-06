using Diarion.Services.Store;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Reads the two preferences the legacy rule needs the moment it is constructed — which
/// <c>App.CreateWindow</c> makes happen before onboarding is even shown — and marks the paid era
/// as seen so the local rule can never fire a second time on this install.
/// </summary>
public sealed class MauiInstallEvidence : IInstallEvidence
{
    internal const string PaidEraSeenKey = "PaidEraSeen";

    public MauiInstallEvidence()
    {
        OnboardingCompletedBeforeThisLaunch = Preferences.Default.Get(App.OnboardingCompletedKey, false);
        IsFirstLaunchOfPaidEra = !Preferences.Default.Get(PaidEraSeenKey, false);
        if (IsFirstLaunchOfPaidEra)
        {
            Preferences.Default.Set(PaidEraSeenKey, true);
        }
    }

    public bool IsFirstLaunchOfPaidEra { get; }

    public bool OnboardingCompletedBeforeThisLaunch { get; }
}
