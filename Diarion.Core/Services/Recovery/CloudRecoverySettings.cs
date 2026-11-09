using System;

namespace Diarion.Services.Recovery;

/// <summary>
/// What the user said, and when the cloud last got a copy. Device-local on purpose, like the
/// scheduled backup's settings: consent given on one phone is not consent on the next.
/// </summary>
public sealed class CloudRecoverySettings
{
    /// <summary>A copy at launch is due again after this long since the last successful one.</summary>
    public static readonly TimeSpan LaunchInterval = TimeSpan.FromDays(1);

    /// <summary>Backgrounding with unsaved changes refreshes the copy no more often than this.</summary>
    public static readonly TimeSpan BackgroundInterval = TimeSpan.FromHours(1);

    public CloudRecoveryDecision Decision { get; set; } = CloudRecoveryDecision.Undecided;

    public bool IsEnabled => Decision == CloudRecoveryDecision.Enabled;

    /// <summary>When the cloud last received a copy. Failures leave it alone, so the next chance retries.</summary>
    public DateTime? LastCopyUtc { get; set; }

    public CloudRecoveryOutcome LastOutcome { get; set; }
}

/// <summary>
/// Nothing leaves the phone until this is <see cref="Enabled"/> (spec 21, D2). The question is asked
/// once; <see cref="Disabled"/> covers both "not now" and a switch turned off later.
/// </summary>
public enum CloudRecoveryDecision
{
    Undecided,
    Enabled,
    Disabled
}

public enum CloudRecoveryOutcome
{
    None,
    Success,
    /// <summary>Switched off, or never switched on.</summary>
    Disabled,
    NotDue,
    /// <summary>No iCloud on this phone right now: signed out, or the container is not reachable.</summary>
    Unavailable,
    Failed
}

/// <summary>Where the settings live: preferences on the device.</summary>
public interface ICloudRecoverySettingsStore
{
    CloudRecoverySettings Load();

    void Save(CloudRecoverySettings settings);
}
