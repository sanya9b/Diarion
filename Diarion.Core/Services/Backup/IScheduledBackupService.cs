using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Backup;

/// <summary>
/// Portable, passphrase-protected backups written to a folder of the user's choosing on a
/// schedule — checked when the app opens, because real background execution on iOS has twice been
/// measured as impractical, and a backup that runs "when you open the app" is still a backup.
/// </summary>
public interface IScheduledBackupService
{
    /// <summary>False where the platform has no folder provider; the settings card then explains rather than offers.</summary>
    bool IsSupported { get; }

    /// <summary>The live settings. Read for display; changed only through the methods below.</summary>
    ScheduledBackupSettings Settings { get; }

    bool IsDue(DateTime nowUtc);

    /// <summary>What the app calls on launch: a backup if one is due, otherwise nothing.</summary>
    Task<ScheduledBackupOutcome> RunIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>What "back up now" calls: a backup regardless of the interval, if the feature is on.</summary>
    Task<ScheduledBackupOutcome> RunNowAsync(CancellationToken cancellationToken = default);

    Task<BackupFolderPick?> PickFolderAsync();

    void Enable(BackupFolderPick folder, string passphrase, int intervalDays);

    /// <summary>Switches off and forgets the passphrase; the folder choice is kept for next time.</summary>
    void Disable();

    void SetInterval(int days);

    void SetFolder(BackupFolderPick folder);
}
