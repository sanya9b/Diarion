using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class ModelDownloadService : IModelDownloadService
{
    /// <summary>Extension for a file still being fetched, so a half-download is never mistaken for a model.</summary>
    private const string PartialSuffix = ".partial";

    private const int CopyBufferBytes = 128 * 1024;

    /// <summary>
    /// How long a socket may deliver nothing before the download is declared dead.
    /// </summary>
    /// <remarks>
    /// A phone that goes through a tunnel, or an app that iOS suspended and resumed, leaves a
    /// connection that is open and silent. Without this the read simply waits — and the user
    /// watches a progress bar that will never move again, with no error and nothing to press.
    /// Sixty seconds is long enough to survive a bad minute of mobile signal and short enough that
    /// nobody sits through it twice.
    /// </remarks>
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Separate and shorter: a server that has not sent headers yet is not slow, it is absent.</summary>
    private static readonly TimeSpan DefaultResponseHeadersTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Report at most once per megabyte. Per 128 KB chunk would be some nine thousand marshalled
    /// callbacks for the 1.1 GB model, all of them to move a progress bar by a hair.
    /// </summary>
    private const long ProgressReportIntervalBytes = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IAiModelPathProvider _paths;
    private readonly IProfileService _profiles;
    private readonly INetworkStatusService _network;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeSpan _responseHeadersTimeout;

    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveDownload> _active = new(StringComparer.Ordinal);

    /// <param name="stallTimeout">Overridable so a test can prove the watchdog fires without waiting a minute for it.</param>
    /// <param name="responseHeadersTimeout">Likewise.</param>
    public ModelDownloadService(
        HttpClient httpClient,
        IAiModelPathProvider paths,
        IProfileService profiles,
        INetworkStatusService network,
        TimeSpan? stallTimeout = null,
        TimeSpan? responseHeadersTimeout = null)
    {
        _httpClient = httpClient;
        _paths = paths;
        _profiles = profiles;
        _network = network;
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
        _responseHeadersTimeout = responseHeadersTimeout ?? DefaultResponseHeadersTimeout;
    }

    public event EventHandler<ModelDownloadProgress>? ProgressChanged;

    public ModelInstallState GetState(AiModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Asked before the disk, because a download that has just started has produced no partial
        // yet, and a row that says "not installed" under a running download reads as a broken button.
        if (IsActive(model.Id))
        {
            return ModelInstallState.Downloading;
        }

        var directory = _paths.GetModelDirectory(model.Id);

        if (model.Files.Any(f => File.Exists(Path.Combine(directory, f.LocalName + PartialSuffix))))
        {
            // Bytes on disk and nobody fetching them. This used to answer Downloading, which left
            // the settings row with no working button at all: cancel had nothing to cancel, and
            // both download and delete were hidden behind a state that never changed. Reinstalling
            // the app was the only way out.
            return ModelInstallState.Interrupted;
        }

        var present = model.Files.Select(f => new FileInfo(Path.Combine(directory, f.LocalName))).ToList();
        if (present.All(f => !f.Exists))
        {
            return ModelInstallState.NotInstalled;
        }

        if (present.Any(f => !f.Exists))
        {
            // Some files arrived and verified, others never started. Resumable, same as a partial.
            return ModelInstallState.Interrupted;
        }

        // Size is a cheap proxy for the digest, which would mean re-reading 120 MB every time the
        // settings screen renders. The real digest is checked once, at the end of the download.
        var sizes = model.Files.Select(f => f.SizeBytes).ToList();
        return present.Select(f => f.Length).SequenceEqual(sizes)
            ? ModelInstallState.Installed
            : ModelInstallState.Corrupt;
    }

    public bool IsActive(string modelId)
    {
        lock (_gate)
        {
            return _active.ContainsKey(modelId);
        }
    }

    public ModelDownloadProgress ProgressOnDisk(AiModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var directory = _paths.GetModelDirectory(model.Id);
        long onDisk = 0;

        foreach (var file in model.Files)
        {
            var final = new FileInfo(Path.Combine(directory, file.LocalName));
            if (final.Exists)
            {
                onDisk += Math.Min(final.Length, file.SizeBytes);
                continue;
            }

            var partial = new FileInfo(Path.Combine(directory, file.LocalName + PartialSuffix));
            if (partial.Exists)
            {
                onDisk += Math.Min(partial.Length, file.SizeBytes);
            }
        }

        return new ModelDownloadProgress(model.Id, onDisk, model.TotalSizeBytes);
    }

    public ModelDownloadProgress? ActiveProgress(string modelId)
    {
        lock (_gate)
        {
            return _active.TryGetValue(modelId, out var entry) ? entry.Progress : null;
        }
    }

    public async Task<bool> WouldUseMobileDataAsync() =>
        // Network first: it is a property read, and it lets the common case skip the database.
        _network.Current == NetworkStatus.Metered && await IsWifiOnlyAsync().ConfigureAwait(false);

    public Task<ModelDownloadOutcome> StartAsync(AiModelDescriptor model, bool allowMobileData = false)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            if (_active.TryGetValue(model.Id, out var running))
            {
                // Two views, one download. Also covers the double tap.
                return running.Task;
            }

            var entry = new ActiveDownload(this, model.Id, model.TotalSizeBytes);
            _active[model.Id] = entry;
            entry.Task = RunAsync(model, entry, allowMobileData);
            return entry.Task;
        }
    }

    public void Cancel(string modelId)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(modelId, out var entry))
            {
                entry.Cancellation.Cancel();
            }
        }
    }

    public async Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var directory = _paths.GetModelDirectory(model.Id);
        Directory.CreateDirectory(directory);

        var total = model.TotalSizeBytes;
        long completedBytes = 0;

        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var finalPath = Path.Combine(directory, file.LocalName);
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length == file.SizeBytes)
            {
                completedBytes += file.SizeBytes;
                progress?.Report(new ModelDownloadProgress(model.Id, completedBytes, total));
                continue;
            }

            var alreadyHave = completedBytes;
            var succeeded = await DownloadFileAsync(
                model,
                file,
                finalPath,
                bytes => progress?.Report(new ModelDownloadProgress(model.Id, alreadyHave + bytes, total)),
                cancellationToken).ConfigureAwait(false);

            if (!succeeded)
            {
                return false;
            }

            completedBytes += file.SizeBytes;
            progress?.Report(new ModelDownloadProgress(model.Id, completedBytes, total));
        }

        return true;
    }

    public async Task DeleteAsync(AiModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Task<ModelDownloadOutcome>? running = null;
        lock (_gate)
        {
            if (_active.TryGetValue(model.Id, out var entry))
            {
                entry.Cancellation.Cancel();
                running = entry.Task;
            }
        }

        if (running is not null)
        {
            // Awaited, not merely cancelled. The copy holds the partial file open with
            // FileShare.None, so deleting the directory while it unwinds throws; and a writer that
            // outlives the delete recreates what the user just asked to be rid of.
            await running.ConfigureAwait(false);
        }

        var directory = _paths.GetModelDirectory(model.Id);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The registered form of a download: it enforces the user's network preference, reports
    /// progress to the whole app, turns its own failures into an outcome rather than an exception,
    /// and removes itself from the register whatever happens.
    /// </summary>
    private async Task<ModelDownloadOutcome> RunAsync(
        AiModelDescriptor model,
        ActiveDownload entry,
        bool allowMobileData)
    {
        // Read once, at the start. The connection is watched for the whole download because it
        // changes on its own; the preference is not, because changing it means walking to the
        // settings screen, and re-reading the database every megabyte to catch that is not a trade
        // worth making.
        var wifiOnly = !allowMobileData && await IsWifiOnlyAsync().ConfigureAwait(false);

        void OnNetworkChanged(object? sender, NetworkStatus status)
        {
            // Only Metered stops anything: a connection the platform will not classify is not
            // evidence of anything, and refusing on it would break downloads that would have worked.
            if (status != NetworkStatus.Metered)
            {
                return;
            }

            entry.StoppedByMobileData = true;

            try
            {
                entry.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The download finished between the event firing and this line. Nothing to stop.
            }
        }

        if (wifiOnly)
        {
            // Watched, not polled. Wi-Fi can drop during a long silence in the stream, and a check
            // inside the read loop would not run again until the next chunk arrived — which, on the
            // mobile connection we are trying not to use, it promptly would.
            _network.Changed += OnNetworkChanged;
        }

        try
        {
            if (wifiOnly && _network.Current == NetworkStatus.Metered)
            {
                // Refused before a socket is opened, so not one byte of the allowance is spent.
                return ModelDownloadOutcome.BlockedByMobileData;
            }

            var completed = await DownloadAsync(model, entry, entry.Cancellation.Token).ConfigureAwait(false);
            if (completed)
            {
                return ModelDownloadOutcome.Completed;
            }

            // A stall or a bad response, unless the watcher pulled the plug on the way past.
            return entry.StoppedByMobileData ? ModelDownloadOutcome.BlockedByMobileData : ModelDownloadOutcome.Failed;
        }
        catch (OperationCanceledException)
        {
            // Someone asked for this. Which someone decides what the user is told.
            return entry.StoppedByMobileData ? ModelDownloadOutcome.BlockedByMobileData : ModelDownloadOutcome.Cancelled;
        }
        catch (Exception)
        {
            // Every network failure reads the same to the user, and there is nowhere to report it
            // to — this app has no telemetry by design.
            return ModelDownloadOutcome.Failed;
        }
        finally
        {
            // Unsubscribed before the token source is disposed, so the handler cannot be running
            // against a dead one for longer than the guard inside it already covers.
            if (wifiOnly)
            {
                _network.Changed -= OnNetworkChanged;
            }

            lock (_gate)
            {
                // Guarded: a Delete followed by a fresh Start could have replaced the entry.
                if (_active.TryGetValue(model.Id, out var current) && ReferenceEquals(current, entry))
                {
                    _active.Remove(model.Id);
                }
            }

            entry.Cancellation.Dispose();
        }
    }

    /// <summary>
    /// The stored preference, defaulting to Wi-Fi-only whenever the answer cannot be had. A
    /// settings read that fails is not permission to spend someone's data allowance.
    /// </summary>
    private async Task<bool> IsWifiOnlyAsync()
    {
        try
        {
            var profile = await _profiles.GetUserProfileAsync().ConfigureAwait(false);
            return profile?.IsWifiOnlyModelDownload ?? true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task<bool> DownloadFileAsync(
        AiModelDescriptor model,
        AiModelFile file,
        string finalPath,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        var partialPath = finalPath + PartialSuffix;
        var resumeFrom = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        // A partial larger than the expected size is not a partial; it is a mismatch between this
        // build's catalogue and whatever wrote the file. Start over rather than reason about it.
        if (resumeFrom >= file.SizeBytes)
        {
            File.Delete(partialPath);
            resumeFrom = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, model.BuildFileUrl(file));
        if (resumeFrom > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
        }

        using var headers = new CancellationTokenSource(_responseHeadersTimeout);
        using var headersLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, headers.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersLinked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our clock ran out, not the user's patience. A failure, and it has to read as one.
            return false;
        }

        using (response)
        {
            // A server that ignores Range answers 200 with the whole file; honouring the resume
            // offset then would splice the beginning of the file onto itself.
            if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.OK)
            {
                resumeFrom = 0;
            }
            else if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            if (!await CopyToPartialAsync(response, partialPath, resumeFrom, _stallTimeout, reportBytes, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        if (!await MatchesDigestAsync(partialPath, file.Sha256, cancellationToken).ConfigureAwait(false))
        {
            // Keeping a file that failed verification would let the size check in GetState call it
            // installed on the next launch.
            File.Delete(partialPath);
            return false;
        }

        File.Move(partialPath, finalPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Streams the body into the partial file under a watchdog, returning false if the connection
    /// went quiet. Bytes already written stay put — that is what the next resume is built on.
    /// </summary>
    private static async Task<bool> CopyToPartialAsync(
        HttpResponseMessage response,
        string partialPath,
        long resumeFrom,
        TimeSpan stallTimeout,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        using var stall = new CancellationTokenSource(stallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using var destination = new FileStream(
                partialPath,
                resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                CopyBufferBytes,
                useAsync: true);

            var buffer = new byte[CopyBufferBytes];
            var written = resumeFrom;
            var lastReported = resumeFrom;
            int read;

            while ((read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
            {
                // Bytes arrived, so the connection is alive: give it another full window.
                stall.CancelAfter(stallTimeout);

                await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                written += read;

                if (written - lastReported >= ProgressReportIntervalBytes)
                {
                    lastReported = written;
                    reportBytes(written);
                }
            }

            reportBytes(written);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> MatchesDigestAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(actual).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseProgress(ModelDownloadProgress progress) => ProgressChanged?.Invoke(this, progress);

    /// <summary>
    /// One download the whole app can see. Doubles as the <see cref="IProgress{T}"/> sink so the
    /// latest figure is recorded synchronously, before it is announced — a view opening midway
    /// through reads <see cref="Progress"/> and starts at the right place rather than at zero.
    /// </summary>
    private sealed class ActiveDownload : IProgress<ModelDownloadProgress>
    {
        private readonly ModelDownloadService _owner;

        public ActiveDownload(ModelDownloadService owner, string modelId, long totalBytes)
        {
            _owner = owner;
            Progress = new ModelDownloadProgress(modelId, 0, totalBytes);
        }

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<ModelDownloadOutcome> Task { get; set; } =
            System.Threading.Tasks.Task.FromResult(ModelDownloadOutcome.Failed);

        public ModelDownloadProgress Progress { get; private set; }

        /// <summary>
        /// Set by the network watcher before it cancels, because a cancelled token says nothing
        /// about who cancelled it — and "you asked me to stop" and "your Wi-Fi went away" need
        /// opposite things said to the user.
        /// </summary>
        public bool StoppedByMobileData { get; set; }

        public void Report(ModelDownloadProgress value)
        {
            Progress = value;
            _owner.RaiseProgress(value);
        }
    }
}

/// <summary>Where model files live. Implemented in the head project, which knows the app's storage.</summary>
public interface IAiModelPathProvider
{
    string GetModelDirectory(string modelId);
}
