using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>
/// One file, from a URL into a <c>.partial</c> on disk, resuming whatever is already there.
/// </summary>
/// <remarks>
/// This exists for iOS. Android can be asked to keep the process alive (see
/// <see cref="IModelTransferHost"/>) and then the ordinary HTTP loop keeps working; iOS cannot —
/// it suspends the process and the socket dies with it, so the transfer has to belong to the
/// system rather than to us.
///
/// Only the bytes-over-the-wire part is swappable. Everything that makes a download *correct* —
/// the Wi-Fi rule, the SHA-256 check, the register of live downloads, resuming a partial rather
/// than starting over, the meaning of every ending — stays in <see cref="ModelDownloadService"/>
/// and is the same on every platform. A second copy of those rules is exactly what handing the job
/// to <c>DownloadManager</c> or to <c>NSURLSession</c> wholesale would have cost.
/// </remarks>
public interface IModelFileTransfer
{
    /// <summary>
    /// Fetches <paramref name="request"/> to completion.
    /// </summary>
    /// <param name="request">What to fetch and where to put it.</param>
    /// <param name="reportBytes">
    /// How much of this one file is now on disk, counted from zero and including whatever was
    /// resumed. Called as the bytes arrive; the caller decides how often that is worth passing on.
    /// </param>
    /// <returns>
    /// True when the file is complete on disk. False for a refusal or a silence — whatever arrived
    /// stays where it is, because that is what the next resume is built on. Cancellation comes back
    /// as <see cref="OperationCanceledException"/>, so the caller can tell it from a failure.
    /// </returns>
    Task<bool> FetchAsync(
        ModelFileTransferRequest request,
        Action<long> reportBytes,
        CancellationToken cancellationToken);
}

/// <param name="Url">Pinned to a commit SHA by the catalogue. This just fetches it.</param>
/// <param name="PartialPath">
/// Where the bytes go. A file already here is resumed from its length, not replaced — the caller
/// has already thrown away anything that could not be a partial of this file.
/// </param>
/// <param name="ExpectedBytes">The catalogued size, for transports that can be told it up front.</param>
/// <param name="AllowMobileData">
/// Only the iOS transport acts on this, and only because it has to: there the transfer outlives
/// the process, so the app is not around to notice the Wi-Fi go away and the refusal has to be the
/// system's. Everywhere else <see cref="ModelDownloadService"/> watches the connection itself and
/// stops the download the moment it turns metered.
/// </param>
public sealed record ModelFileTransferRequest(
    string Url,
    string PartialPath,
    long ExpectedBytes,
    bool AllowMobileData = false);
