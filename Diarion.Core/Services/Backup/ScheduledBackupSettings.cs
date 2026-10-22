using System;

namespace Diarion.Services.Backup;

/// <summary>
/// Everything the scheduler needs to know, except the passphrase. Device-local on purpose — kept in
/// preferences, not in the database: the folder grant is this device's, and a database restored on
/// a new phone must not arrive believing it can still write to a folder the old phone was given.
/// </summary>
public sealed class ScheduledBackupSettings
{
    public const int DefaultIntervalDays = 7;

    /// <summary>How many of the app's own backup files to leave in the folder; older ones are deleted.</summary>
    public const int DefaultKeepCount = 5;

    public static readonly int[] IntervalChoices = { 1, 3, 7, 14, 30 };

    public bool IsEnabled { get; set; }

    public int IntervalDays { get; set; } = DefaultIntervalDays;

    public int KeepCount { get; set; } = DefaultKeepCount;

    public string? FolderToken { get; set; }

    public string? FolderName { get; set; }

    /// <summary>When the last backup actually succeeded. Failures leave it alone, so the next launch tries again.</summary>
    public DateTime? LastRunUtc { get; set; }

    public ScheduledBackupOutcome LastOutcome { get; set; }

    public static int NormalizeInterval(int days) => days < 1 ? DefaultIntervalDays : days;
}

public enum ScheduledBackupOutcome
{
    None,
    Success,

    /// <summary>Not switched on, or switched on without a folder.</summary>
    Disabled,

    /// <summary>Switched on, but the interval since the last success has not passed.</summary>
    NotDue,

    /// <summary>The grant behind the folder no longer works; the user has to pick one again.</summary>
    FolderUnavailable,

    /// <summary>The secure store lost the passphrase; the user has to set one again.</summary>
    NoPassphrase,

    Failed
}

/// <summary>
/// Where the settings and the passphrase live. Two different places with one interface: the
/// settings are plain preferences, the passphrase is the platform's secure storage, the same place
/// the database key lives — a backup passphrase in preferences would make the backup weaker than
/// the database it copies.
/// </summary>
public interface IScheduledBackupStore
{
    ScheduledBackupSettings Load();

    void Save(ScheduledBackupSettings settings);

    string? GetPassphrase();

    /// <summary>Null removes it.</summary>
    void SetPassphrase(string? passphrase);
}
