using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Recovery;

/// <summary>What is known about the copy in the cloud without downloading it.</summary>
public sealed record RecoveryCopyInfo(DateTime? ModifiedUtc, long? SizeBytes, bool IsDownloaded);

/// <summary>
/// The platform's hold on one file outside the app's sandbox: the recovery copy of the database,
/// encrypted with this device's key (spec 21, D1). On iOS it is the app's iCloud container, which
/// outlives the app and follows the Apple ID to a new phone. The service above decides when to
/// write and whether to restore; this only moves bytes.
/// </summary>
public interface IRecoveryStore
{
    /// <summary>Compiled in for this platform at all. False hides the feature everywhere.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Reachable right now: the entitlement is present and the user is signed in to iCloud. May
    /// take a moment the first time, so never on the UI thread.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for the copy, waiting up to <paramref name="searchTimeout"/> for the cloud's index to
    /// arrive on a freshly installed app. Null when there is none.
    /// </summary>
    Task<RecoveryCopyInfo?> FindAsync(TimeSpan searchTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the copy readable locally, downloading it if the cloud only holds a placeholder, and
    /// returns its path; null when it did not arrive within <paramref name="timeout"/>.
    /// </summary>
    Task<string?> DownloadAsync(TimeSpan timeout, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Replaces the copy with the file at <paramref name="sourcePath"/>.</summary>
    Task WriteAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
