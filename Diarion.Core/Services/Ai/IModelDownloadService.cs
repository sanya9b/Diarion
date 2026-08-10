using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public enum ModelInstallState
{
    NotInstalled,
    Downloading,
    Installed,

    /// <summary>Files are present but a digest did not match, so they are not trusted.</summary>
    Corrupt,

    /// <summary>
    /// Part of the model is on disk and nothing is fetching the rest. A phone suspends the app, a
    /// network drops, a process is killed — and none of that is visible on the next launch except
    /// as a <c>.partial</c> file. Distinct from <see cref="Downloading"/> because the two need
    /// opposite things from the user: one needs waiting, the other needs a tap.
    /// </summary>
    Interrupted,
}

/// <summary>
/// How a download ended. A bool cannot carry this: three of these four leave the same partial file
/// behind, and the only difference the user can act on is which of them it was.
/// </summary>
public enum ModelDownloadOutcome
{
    /// <summary>Every file arrived and verified.</summary>
    Completed,

    /// <summary>The user asked it to stop. Not an error, and says nothing.</summary>
    Cancelled,

    /// <summary>The server or the connection gave out. Bytes kept for a resume.</summary>
    Failed,

    /// <summary>
    /// Wi-Fi-only is on and mobile data is all there is — either at the start, or because Wi-Fi
    /// dropped partway through. Bytes kept, and the user is told why it stopped rather than being
    /// shown a failure they did not cause.
    /// </summary>
    BlockedByMobileData,
}

/// <summary>
/// What a download is busy with. The two are indistinguishable on a progress bar — both leave it
/// pinned at the same place — and the second can take ten seconds on a phone, which is long enough
/// to be read as a freeze.
/// </summary>
public enum ModelDownloadPhase
{
    /// <summary>Bytes are arriving.</summary>
    Transferring,

    /// <summary>The file is complete and its SHA-256 is being computed.</summary>
    Verifying,
}

/// <param name="BytesReceived">Across all of the model's files, including bytes resumed from disk.</param>
/// <param name="BytesPerSecond">
/// Measured over a sliding window rather than since the start, and zero until there is enough to
/// measure. Defaulted so every existing three-argument construction still compiles.
/// </param>
public readonly record struct ModelDownloadProgress(
    string ModelId,
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond = 0d,
    ModelDownloadPhase Phase = ModelDownloadPhase.Transferring)
{
    public double Fraction => TotalBytes <= 0 ? 0d : Math.Clamp((double)BytesReceived / TotalBytes, 0d, 1d);

    /// <summary>
    /// How long the rest should take at the rate just measured, or null when there is nothing to
    /// base that on. Never a countdown derived from a rate that stopped being true minutes ago.
    /// </summary>
    public TimeSpan? Remaining =>
        BytesPerSecond <= 0d || TotalBytes <= 0 || BytesReceived >= TotalBytes
            ? null
            : TimeSpan.FromSeconds((TotalBytes - BytesReceived) / BytesPerSecond);
}

/// <summary>
/// Fetches model files from HuggingFace into the app's private storage.
/// </summary>
/// <remarks>
/// This is the only component in the app that opens a socket, and it only ever performs GETs
/// against pinned commit URLs. Nothing about the user is sent — no identifiers, no query strings,
/// no telemetry. That constraint is the entire justification for the INTERNET permission.
///
/// It also owns the lifetime of a download, which is not where that lifetime naturally wants to
/// live. A gigabyte takes minutes; the settings page is transient and rebuilds itself every time it
/// appears. When the page owned the <see cref="CancellationTokenSource"/>, walking away and coming
/// back produced a row that said "downloading" over a task it could neither see nor stop. So the
/// singleton holds it, and any number of short-lived views can attach to the same download.
/// </remarks>
public interface IModelDownloadService
{
    /// <summary>
    /// Raised as bytes arrive, off the UI thread. Subscribers that touch bindings must marshal.
    /// </summary>
    event EventHandler<ModelDownloadProgress>? ProgressChanged;

    /// <summary>
    /// Whether a download here survives the app being minimized or the screen going dark. What the
    /// settings row is allowed to promise — and it must not promise it where the platform will not
    /// keep it.
    /// </summary>
    bool KeepsRunningInBackground { get; }

    ModelInstallState GetState(AiModelDescriptor model);

    /// <summary>True while this process is actively fetching the model.</summary>
    bool IsActive(string modelId);

    /// <summary>
    /// Where the in-flight download has got to, or null when nothing is running. Lets a view that
    /// opened halfway through show the real figure instead of starting its progress bar at zero.
    /// </summary>
    ModelDownloadProgress? ActiveProgress(string modelId);

    /// <summary>
    /// How much of the model is already on disk, whole files and partials together. What an
    /// interrupted download has to show for itself, and the answer to "will pressing download
    /// again cost me the whole gigabyte" — it will not.
    /// </summary>
    ModelDownloadProgress ProgressOnDisk(AiModelDescriptor model);

    /// <summary>
    /// Whether starting a download right now would spend mobile data against the user's wishes —
    /// the Wi-Fi-only preference is on and cellular is the only route.
    /// </summary>
    /// <remarks>
    /// Asked by the settings row so it can offer the choice before anything starts, and enforced
    /// again inside <see cref="StartAsync"/> so the rule holds for a caller that never asked. One
    /// owner, because a rule stated in two places drifts.
    /// </remarks>
    Task<bool> WouldUseMobileDataAsync();

    /// <summary>
    /// Starts the download, or hands back the one already in flight for this model. Awaiting the
    /// returned task is how a second view learns that a download it did not start has finished.
    /// Never throws for cancellation — every ending, including that one, comes back as an outcome.
    /// </summary>
    /// <param name="allowMobileData">
    /// Set once the user has been asked and said yes. Applies to this download only; the stored
    /// preference is untouched, so the next model asks again.
    /// </param>
    Task<ModelDownloadOutcome> StartAsync(AiModelDescriptor model, bool allowMobileData = false);

    /// <summary>
    /// Stops the in-flight download, if there is one. Bytes already written stay on disk, so the
    /// next attempt resumes rather than starting the gigabyte again.
    /// </summary>
    void Cancel(string modelId);

    /// <summary>
    /// Downloads whatever is missing, resuming partial files, and verifies every digest.
    /// Returns false if any file failed to arrive or failed verification.
    /// </summary>
    /// <remarks>
    /// The unmanaged form: the caller owns the cancellation and gets the exception. Prefer
    /// <see cref="StartAsync"/> from UI code, which registers the download so the rest of the app
    /// can see it.
    /// </remarks>
    Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the model's files, partial ones included, stopping any download first and waiting
    /// for it to let go — a running download holds an open handle, and a directory cannot be
    /// removed out from under one.
    /// </summary>
    Task DeleteAsync(AiModelDescriptor model);
}
