using System;
using System.Globalization;
using Diarion.Services.Recovery;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>Consent and the last copy's time, in device preferences (see <see cref="CloudRecoverySettings"/>).</summary>
public sealed class PreferencesCloudRecoveryStore : ICloudRecoverySettingsStore
{
    private const string Prefix = "cloud_recovery_";

    public CloudRecoverySettings Load()
    {
        var settings = new CloudRecoverySettings
        {
            Decision = Enum.TryParse<CloudRecoveryDecision>(Preferences.Default.Get(Prefix + "decision", string.Empty), out var decision)
                ? decision
                : CloudRecoveryDecision.Undecided,
            LastOutcome = Enum.TryParse<CloudRecoveryOutcome>(Preferences.Default.Get(Prefix + "last_outcome", string.Empty), out var outcome)
                ? outcome
                : CloudRecoveryOutcome.None
        };

        var last = Preferences.Default.Get(Prefix + "last_copy_utc", string.Empty);
        if (DateTime.TryParseExact(last, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            settings.LastCopyUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return settings;
    }

    public void Save(CloudRecoverySettings settings)
    {
        Preferences.Default.Set(Prefix + "decision", settings.Decision.ToString());
        Preferences.Default.Set(Prefix + "last_outcome", settings.LastOutcome.ToString());
        Preferences.Default.Set(
            Prefix + "last_copy_utc",
            settings.LastCopyUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
