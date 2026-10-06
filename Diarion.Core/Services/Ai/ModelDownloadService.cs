using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    private readonly IModelFileTransfer _transfer;
    private readonly IAiModelPathProvider _paths;
    private readonly IProfileService _profiles;
    private readonly INetworkStatusService _network;
    private readonly IModelTransferHost _host;

    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveDownload> _active = new(StringComparer.Ordinal);

    /// <param name="transfer">Moves the bytes. The only part of a download that differs by platform.</param>
    /// <param name="host">Keeps the process alive on platforms that would otherwise suspend it mid-download.</param>
    public ModelDownloadService(
        IModelFileTransfer transfer,
        IAiModelPathProvider paths,
        IProfileService profiles,
        INetworkStatusService network,
        IModelTransferHost host)
    {
        _transfer = transfer;
        _paths = paths;
        _profiles = profiles;
        _network = network;
        _host = host;
    }

    public event EventHandler<ModelDownloadProgress>? ProgressChanged;

    public bool KeepsRunningInBackground => _host.KeepsRunningInBackground;

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

    /// <remarks>
    /// The unmanaged path: no register, no Wi-Fi rule, and so — for the transports that can be told
    /// — no objection to mobile data either. Everything that enforces the user's preference goes
    /// through <see cref="StartAsync"/>.
    /// </remarks>
    public Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        DownloadAsync(model, progress, allowMobileData: true, cancellationToken);

    private async Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress,
        bool allowMobileData,
        CancellationToken cancellationToken)
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
            var lastReported = completedBytes;

            void ReportBytes(long bytes)
            {
                lastReported = alreadyHave + bytes;
                progress?.Report(new ModelDownloadProgress(model.Id, lastReported, total));
            }

            // Carries the byte count rather than recomputing it, so a phase change never rewinds
            // or advances the bar on its way past.
            void ReportPhase(ModelDownloadPhase phase) =>
                progress?.Report(new ModelDownloadProgress(model.Id, lastReported, total, 0d, phase));

            var succeeded = await DownloadFileAsync(
                model,
                file,
                finalPath,
                allowMobileData,
                ReportBytes,
                ReportPhase,
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
        // StartAsync builds this task while holding the registry lock, and the first thing below
        // reaches into the platform — a binder call, on Android. That has no business running
        // under a lock that Cancel and IsActive also take.
        await Task.Yield();

        // Held for the whole download, on the platforms where being minimized would otherwise end
        // it. Released in the finally, whichever way this ends.
        var host = BeginHost(model);

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

            var completed = await DownloadAsync(model, entry, !wifiOnly, entry.Cancellation.Token).ConfigureAwait(false);
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

            try
            {
                // Last, so the notification does not vanish before the registry says the download
                // is over — the row and the notification would disagree for that instant.
                host.Dispose();
            }
            catch (Exception)
            {
                // A platform that cannot tidy up its own notification is not a failed download.
            }
        }
    }

    /// <summary>
    /// Never throws: a platform that refuses to keep us alive is a reason to download in the
    /// foreground, not a reason to fail before the first byte.
    /// </summary>
    private IDisposable BeginHost(AiModelDescriptor model)
    {
        try
        {
            return _host.Begin(model.Id, model.DisplayName);
        }
        catch (Exception)
        {
            return NullModelTransferHost.Empty;
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
        bool allowMobileData,
        Action<long> reportBytes,
        Action<ModelDownloadPhase> reportPhase,
        CancellationToken cancellationToken)
    {
        var partialPath = finalPath + PartialSuffix;

        // What is already on disk decides whether there is anything to fetch. Checked here rather
        // than in the transport so that both transports inherit the same answer.
        var onDisk = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        // Past the catalogued size it is not a partial of this file at all — a mismatch between
        // this build's catalogue and whatever wrote it. Start over rather than reason about it.
        if (onDisk > file.SizeBytes)
        {
            File.Delete(partialPath);
            onDisk = 0;
        }

        // Exactly the catalogued size is a different story: the bytes are all here and only the
        // digest has not been checked. That is an ordinary ending on iOS, where the system finishes
        // the transfer whether or not the app is still alive, and it is reachable anywhere a process
        // dies in the seconds between the last write and the verification. Re-fetching a gigabyte
        // that is already on the disk would be the wrong way to find out it is sound.
        if (onDisk < file.SizeBytes)
        {
            var request = new ModelFileTransferRequest(
                model.BuildFileUrl(file),
                partialPath,
                file.SizeBytes,
                allowMobileData);

            if (!await _transfer.FetchAsync(request, reportBytes, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }
        else
        {
            reportBytes(onDisk);
        }

        // Announced, because hashing a gigabyte on a phone is ten seconds of a bar that has
        // stopped moving, and a bar that has stopped is indistinguishable from an app that has.
        reportPhase(ModelDownloadPhase.Verifying);

        var verified = await MatchesDigestAsync(partialPath, file.Sha256, cancellationToken).ConfigureAwait(false);
        reportPhase(ModelDownloadPhase.Transferring);

        if (!verified)
        {
            // Keeping a file that failed verification would let the size check in GetState call it
            // installed on the next launch.
            File.Delete(partialPath);
            return false;
        }

        File.Move(partialPath, finalPath, overwrite: true);
        return true;
    }

    private static async Task<bool> MatchesDigestAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(actual).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseProgress(ModelDownloadProgress progress)
    {
        try
        {
            // The platform first: when the app is minimized its notification is the only thing the
            // user can see, and the in-app subscribers are drawing to a window nobody is looking at.
            _host.Report(progress);
        }
        catch (Exception)
        {
            // A notification that refuses to update is not worth losing a download over.
        }

        ProgressChanged?.Invoke(this, progress);
    }

    /// <summary>
    /// One download the whole app can see. Doubles as the <see cref="IProgress{T}"/> sink so the
    /// latest figure is recorded synchronously, before it is announced — a view opening midway
    /// through reads <see cref="Progress"/> and starts at the right place rather than at zero.
    /// </summary>
    private sealed class ActiveDownload : IProgress<ModelDownloadProgress>
    {
        private readonly ModelDownloadService _owner;

        /// <summary>Monotonic, unlike the wall clock, which a phone can move under a long download.</summary>
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly TransferRateMeter _rate = new();

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
            if (value.Phase == ModelDownloadPhase.Transferring)
            {
                value = value with { BytesPerSecond = _rate.Observe(_clock.Elapsed, value.BytesReceived) };
            }
            else
            {
                // Hashing moves no bytes. Carrying the window across that pause would report the
                // wait as a collapse in speed the moment the next file starts arriving.
                _rate.Reset();
            }

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
