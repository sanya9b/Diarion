using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Recovery;

/// <summary>How a restore from the cloud copy ended.</summary>
public enum RecoveryRestoreOutcome
{
    Restored,
    /// <summary>The cloud holds no copy.</summary>
    NotFound,
    /// <summary>There is a copy, but it did not arrive in time.</summary>
    DownloadFailed,
    /// <summary>The copy opens with another device's key: made on a phone whose keychain did not sync here.</summary>
    KeyMismatch,
    /// <summary>Written by a newer schema than this build knows.</summary>
    NewerSchema,
    Failed
}

/// <summary>
/// The recovery copy's keeper (spec 21): writes the database, encrypted with this device's key, to
/// the platform's cloud container once the user has agreed, and puts it back on a fresh install.
/// Separate from the passphrase backups on purpose — those are the user's files, portable by hand;
/// this one is the app's, and opens only where the key is.
/// </summary>
public interface ICloudRecoveryService
{
    /// <summary>A cloud container exists on this platform at all.</summary>
    bool IsSupported { get; }

    /// <summary>Signed in and reachable right now. Off the UI thread: the first call may take a moment.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    CloudRecoverySettings Settings { get; }

    /// <summary>True until the user has answered the question, on a platform where it can be asked.</summary>
    bool NeedsDecision { get; }

    /// <summary>The user said yes: remembers it and makes the first copy at once.</summary>
    Task<CloudRecoveryOutcome> EnableAsync(CancellationToken cancellationToken = default);

    /// <summary>The user said no, or not now. Nothing is written; the switch in settings remains.</summary>
    void Decline();

    /// <summary>Stops the copies; optionally removes the one already in the cloud.</summary>
    Task DisableAsync(bool deleteCopy, CancellationToken cancellationToken = default);

    Task<CloudRecoveryOutcome> CopyNowAsync(CancellationToken cancellationToken = default);

    /// <summary>The launch check: a copy if enabled and the daily interval has passed.</summary>
    Task<CloudRecoveryOutcome> RunIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Something was saved since the last copy. Cheap; called from the save messages.</summary>
    void MarkChanged();

    /// <summary>The app is going to the background: a copy if something changed and an hour has passed.</summary>
    Task<CloudRecoveryOutcome> FlushOnBackgroundAsync(CancellationToken cancellationToken = default);

    Task<RecoveryCopyInfo?> FindCopyAsync(TimeSpan searchTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the copy, checks that this device's key opens it and that its schema is not newer,
    /// and swaps it in under every open screen. A success turns the feature on: the copy the user
    /// just chose to restore from is the one they want kept.
    /// </summary>
    Task<RecoveryRestoreOutcome> RestoreAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task DeleteCopyAsync(CancellationToken cancellationToken = default);
}
