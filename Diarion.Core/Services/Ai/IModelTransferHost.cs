namespace Diarion.Services.Ai;

/// <summary>
/// Asks the platform to let a download outlive the visible app.
/// </summary>
/// <remarks>
/// Nothing in this app cancels a download when the user navigates away — the service is a
/// singleton, the settings row owns none of it, and sleep does not touch it. The operating system
/// does: Android takes CPU and network from a process that left the foreground, and iOS suspends
/// the process along with its sockets. A gigabyte cannot arrive behind a locked screen without
/// asking permission first, and what has to be asked differs per platform — a foreground service
/// on Android, a background session on iOS. This is where the Core download loop does the asking
/// without knowing which.
///
/// The default implementation asks for nothing, which is the right answer on desktop: Windows and
/// Mac do not suspend a windowed process for being minimized.
/// </remarks>
public interface IModelTransferHost
{
    /// <summary>
    /// Whether a download survives the app being minimized on this platform. Drives what the model
    /// row promises, which must never be a promise the platform will not keep.
    /// </summary>
    bool KeepsRunningInBackground { get; }

    /// <summary>
    /// Keeps the process alive until the returned handle is disposed. Implementations that need
    /// nothing return an empty handle; disposing any handle twice is safe.
    /// </summary>
    IDisposable Begin(string modelId, string modelName);

    /// <summary>The latest figures, for whatever the platform shows while the app is away.</summary>
    void Report(ModelDownloadProgress progress);
}

/// <summary>The desktop answer, and the fallback wherever a platform host is not registered.</summary>
public sealed class NullModelTransferHost : IModelTransferHost
{
    /// <summary>A handle that holds nothing, shared because there is nothing to hold.</summary>
    public static IDisposable Empty { get; } = new NoHandle();

    public bool KeepsRunningInBackground => false;

    public IDisposable Begin(string modelId, string modelName) => Empty;

    public void Report(ModelDownloadProgress progress)
    {
    }

    private sealed class NoHandle : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
