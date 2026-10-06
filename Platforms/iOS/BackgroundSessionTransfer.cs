using System.Collections.Generic;
using System.IO;
using Foundation;
using Microsoft.Extensions.DependencyInjection;

namespace Diarion.Services.Ai;

/// <summary>
/// The iOS answer to "can it download while minimized". The transfer is handed to the system, which
/// keeps going after the app is suspended and, if it has to, after the app is gone.
/// </summary>
/// <remarks>
/// Android could be asked to keep the process alive; iOS cannot. It suspends the process and the
/// socket dies with it, so no foreground trick helps — the only transport that survives a locked
/// screen is <c>NSURLSession</c> on a background configuration, where a system daemon does the
/// fetching and hands the finished file back.
///
/// What this deliberately does <b>not</b> take over: resuming, verifying, the Wi-Fi rule, the
/// meaning of an ending. All of that stays in <see cref="ModelDownloadService"/>, identical on
/// every platform. This class fetches one file and says whether it arrived.
///
/// The promise is suspension, not immortality. If the system kills the process outright the awaited
/// task dies with it — but the bytes do not: the destination travels in the task's description,
/// which the system keeps, so a download that finishes while the app is dead still lands in its
/// <c>.partial</c>. The next launch reads that as <see cref="ModelInstallState.Interrupted"/> and
/// one tap verifies and finishes it.
/// </remarks>
public sealed class BackgroundSessionTransfer : IModelFileTransfer
{
    /// <summary>
    /// Stable across launches on purpose: recreating a session with the same identifier is how the
    /// system reconnects an app to transfers it started in a previous life.
    /// </summary>
    private const string WifiSessionId = "com.diarion.app.model-downloads";

    private const string CellularSessionId = "com.diarion.app.model-downloads.cellular";

    private const int CopyBufferBytes = 128 * 1024;

    private static readonly object InstanceGate = new();
    private static BackgroundSessionTransfer? _instance;

    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// The app's data directory as it is <em>this</em> launch. Destinations are remembered relative
    /// to it, because the container path carries a UUID that iOS is free to change between
    /// launches — an absolute path written yesterday can point at nothing today.
    /// </summary>
    private readonly string _baseDirectory;

    public BackgroundSessionTransfer(string baseDirectory)
    {
        _baseDirectory = Path.TrimEndingDirectorySeparator(baseDirectory);

        lock (InstanceGate)
        {
            // The container resolves this as a singleton; the static handle exists only so that
            // AppDelegate can reach it from a callback that carries no services with it.
            _instance = this;
        }
    }

    /// <summary>
    /// Called by <c>AppDelegate</c> when the system wakes the app to deliver transfers it finished
    /// on its own. Recreating the session is what makes the delegate callbacks arrive; without this
    /// the finished file is simply discarded.
    /// </summary>
    public static void HandleEventsForBackgroundSession(string sessionIdentifier, Action completionHandler)
    {
        BackgroundSessionTransfer? instance;
        lock (InstanceGate)
        {
            instance = _instance;
        }

        // Usually null on this path. The app has just been woken solely to receive a file, so
        // nothing has asked the container for a downloader yet and the singleton does not exist —
        // asking now both builds it and, through its constructor, fills the handle above.
        instance ??= IPlatformApplication.Current?.Services.GetService<IModelFileTransfer>()
            as BackgroundSessionTransfer;

        if (instance is null)
        {
            // No app behind the callback. Nothing can be delivered, and holding the handler would
            // leave the system waiting on a reply that is never coming.
            completionHandler();
            return;
        }

        instance.Attach(sessionIdentifier, completionHandler);
    }

    public Task<bool> FetchAsync(
        ModelFileTransferRequest request,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reportBytes);

        cancellationToken.ThrowIfCancellationRequested();

        var resumeFrom = File.Exists(request.PartialPath)
            ? new FileInfo(request.PartialPath).Length
            : 0L;

        var session = Resolve(request.AllowMobileData ? CellularSessionId : WifiSessionId);
        return session.FetchAsync(request, resumeFrom, reportBytes, cancellationToken);
    }

    /// <summary>Resolves the destination a task remembered, refusing anything that points outside our own storage.</summary>
    private string? ResolveDestination(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(_baseDirectory, description));
        return combined.StartsWith(_baseDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? combined
            : null;
    }

    private string Describe(string partialPath) => Path.GetRelativePath(_baseDirectory, partialPath);

    private Session Resolve(string identifier)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(identifier, out var session))
            {
                session = new Session(this, identifier, allowsCellular: identifier == CellularSessionId);
                _sessions[identifier] = session;
            }

            return session;
        }
    }

    private void Attach(string identifier, Action completionHandler) =>
        Resolve(identifier).AttachSystemHandler(completionHandler);

    /// <summary>One background configuration, its <c>NSURLSession</c>, and the fetches waiting on it.</summary>
    private sealed class Session
    {
        private readonly BackgroundSessionTransfer _owner;
        private readonly NSUrlSession _session;
        private readonly object _gate = new();
        private readonly Dictionary<nuint, PendingFetch> _pending = new();

        private Action? _systemHandler;

        public Session(BackgroundSessionTransfer owner, string identifier, bool allowsCellular)
        {
            _owner = owner;

            var configuration = NSUrlSessionConfiguration.CreateBackgroundSessionConfiguration(identifier);
            configuration.AllowsCellularAccess = allowsCellular;

            // Not discretionary: the user has just tapped Download and is watching. Letting the
            // system pick "a good time" would look exactly like nothing happening.
            configuration.Discretionary = false;
            configuration.SessionSendsLaunchEvents = true;

            // Off the main thread: a finished file gets appended to the partial inside a delegate
            // callback, and that is a gigabyte of copying the UI has no business waiting for.
            var queue = new NSOperationQueue { MaxConcurrentOperationCount = 1 };
            _session = NSUrlSession.FromConfiguration(configuration, new Callbacks(this), queue);
        }

        public Task<bool> FetchAsync(
            ModelFileTransferRequest request,
            long resumeFrom,
            Action<long> reportBytes,
            CancellationToken cancellationToken)
        {
            var message = new NSMutableUrlRequest(new NSUrl(request.Url));
            if (resumeFrom > 0)
            {
                message.Headers = NSDictionary.FromObjectAndKey(
                    new NSString($"bytes={resumeFrom}-"),
                    new NSString("Range"));
            }

            var task = _session.CreateDownloadTask(message);

            // Survives the process. This is the only reason a download that finishes while the app
            // is dead still knows where to land.
            task.TaskDescription = _owner.Describe(request.PartialPath);

            var pending = new PendingFetch(request.PartialPath, resumeFrom, reportBytes);

            lock (_gate)
            {
                _pending[task.TaskIdentifier] = pending;
            }

            // Registered before Resume so a token that is already cancelled cannot leave a task
            // running with nobody waiting on it.
            pending.Attach(cancellationToken.Register(task.Cancel));
            task.Resume();

            return pending.Completion;
        }

        public void AttachSystemHandler(Action completionHandler)
        {
            Action? previous;
            lock (_gate)
            {
                previous = _systemHandler;
                _systemHandler = completionHandler;
            }

            // Two wake-ups before the first was answered. Answering the older one immediately keeps
            // the system from holding the app awake waiting for a reply that is never coming.
            previous?.Invoke();
        }

        private void OnWroteData(NSUrlSessionDownloadTask task, long totalBytesWritten)
        {
            PendingFetch? pending;
            lock (_gate)
            {
                _pending.TryGetValue(task.TaskIdentifier, out pending);
            }

            pending?.Report(totalBytesWritten);
        }

        /// <summary>
        /// The file has arrived in a temporary location that stops existing the moment this returns,
        /// so the move happens here and synchronously — which is also why this queue is not the
        /// main one.
        /// </summary>
        private void OnFinishedDownloading(NSUrlSessionDownloadTask task, NSUrl location)
        {
            PendingFetch? pending;
            lock (_gate)
            {
                _pending.TryGetValue(task.TaskIdentifier, out pending);
            }

            // Absent for a task the system is delivering to a process that was rebuilt since it
            // started. The bytes are still worth keeping, and the description says where.
            var destination = pending?.PartialPath ?? _owner.ResolveDestination(task.TaskDescription);
            var temporary = location.Path;

            if (destination is null || temporary is null)
            {
                pending?.Landed(false);
                return;
            }

            var status = (int)((task.Response as NSHttpUrlResponse)?.StatusCode ?? 0);
            if (status is < 200 or >= 300)
            {
                // A 404 or a 416 still arrives as a downloaded "file" — the error page. Writing it
                // into the partial would poison the resume and fail the digest much later.
                pending?.Landed(false);
                return;
            }

            try
            {
                Append(temporary, destination, appendToExisting: status == 206);
                pending?.Landed(true);
            }
            catch (IOException)
            {
                pending?.Landed(false);
            }
            catch (UnauthorizedAccessException)
            {
                pending?.Landed(false);
            }
        }

        private void OnCompleted(NSUrlSessionTask task, NSError? error)
        {
            PendingFetch? pending;
            lock (_gate)
            {
                // Taken rather than read, so a second callback for the same task cannot resolve it
                // twice and cannot leak the entry.
                _pending.Remove(task.TaskIdentifier, out pending);
            }

            pending?.Finish(error);
        }

        private void OnFinishedEvents()
        {
            Action? handler;
            lock (_gate)
            {
                handler = _systemHandler;
                _systemHandler = null;
            }

            if (handler is null)
            {
                return;
            }

            // The system requires this on the main thread, and it is what lets the app go back to
            // sleep instead of being killed for staying awake.
            NSRunLoop.Main.BeginInvokeOnMainThread(() => handler());
        }

        /// <summary>
        /// Moves the finished body into the partial file. A rename when there is nothing to keep —
        /// instant, and it does not need a second gigabyte of free space — and a copy when the
        /// server honoured our <c>Range</c> and this is the rest of a file we already have part of.
        /// </summary>
        private static void Append(string temporary, string destination, bool appendToExisting)
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!appendToExisting || !File.Exists(destination))
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }

            using var input = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes);
            using var output = new FileStream(destination, FileMode.Append, FileAccess.Write, FileShare.None, CopyBufferBytes);
            input.CopyTo(output, CopyBufferBytes);
        }

        /// <summary>
        /// Separate from <see cref="Session"/> because <c>NSUrlSession</c> retains its delegate for
        /// the life of the session, and a delegate that is also the session would never be collected.
        /// </summary>
        private sealed class Callbacks(Session owner) : NSUrlSessionDownloadDelegate
        {
            public override void DidWriteData(
                NSUrlSession session,
                NSUrlSessionDownloadTask downloadTask,
                long bytesWritten,
                long totalBytesWritten,
                long totalBytesExpectedToWrite) =>
                owner.OnWroteData(downloadTask, totalBytesWritten);

            public override void DidFinishDownloading(
                NSUrlSession session,
                NSUrlSessionDownloadTask downloadTask,
                NSUrl location) =>
                owner.OnFinishedDownloading(downloadTask, location);

            public override void DidCompleteWithError(NSUrlSession session, NSUrlSessionTask task, NSError? error) =>
                owner.OnCompleted(task, error);

            public override void DidFinishEventsForBackgroundSession(NSUrlSession session) =>
                owner.OnFinishedEvents();
        }
    }

    /// <summary>One awaited file: where it goes, how far it had got, and who is listening.</summary>
    private sealed class PendingFetch(string partialPath, long resumeFrom, Action<long> reportBytes)
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private CancellationTokenRegistration _cancellation;
        private bool _landed;

        public string PartialPath { get; } = partialPath;

        public Task<bool> Completion => _completion.Task;

        public void Attach(CancellationTokenRegistration registration) => _cancellation = registration;

        public void Report(long totalBytesWritten) => reportBytes(resumeFrom + totalBytesWritten);

        /// <summary>Recorded rather than completed: the error, if there is one, arrives afterwards.</summary>
        public void Landed(bool success) => _landed = success;

        public void Finish(NSError? error)
        {
            _cancellation.Dispose();

            if (error is not null)
            {
                if (error.Code == (nint)NSUrlError.Cancelled)
                {
                    _completion.TrySetCanceled();
                    return;
                }

                _completion.TrySetResult(false);
                return;
            }

            if (_landed)
            {
                // The truth from disk rather than from the counter: a server that ignored Range
                // sent the whole file, and the running total would have counted the resumed bytes
                // twice.
                try
                {
                    reportBytes(new FileInfo(PartialPath).Length);
                }
                catch (IOException)
                {
                    // A figure for a progress bar is not worth failing a finished download over.
                }
            }

            _completion.TrySetResult(_landed);
        }
    }
}
