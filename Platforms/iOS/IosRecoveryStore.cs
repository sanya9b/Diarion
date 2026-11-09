using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services.Recovery;
using Foundation;

namespace Diarion.Services;

/// <summary>
/// The recovery copy in the app's iCloud container (spec 21, D1): <c>Recovery/diarion-recovery.db</c>
/// at the container root, which the Files app does not show — the file opens only with this
/// phone's key and is nothing a person could use by hand. Writes go through
/// <see cref="NSFileCoordinator"/> so iCloud uploads them promptly.
/// </summary>
public sealed class IosRecoveryStore : IRecoveryStore
{
    private const string Folder = "Recovery";
    private const string FileName = "diarion-recovery.db";

    /// <summary>iCloud's stand-in for a file it has not downloaded yet.</summary>
    private const string PlaceholderName = "." + FileName + ".icloud";

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(400);

    public bool IsSupported => true;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await ICloudContainer.RootAsync().ConfigureAwait(false) is not null;

    public async Task<RecoveryCopyInfo?> FindAsync(TimeSpan searchTimeout, CancellationToken cancellationToken = default)
    {
        var paths = await PathsAsync().ConfigureAwait(false);
        if (paths is null) return null;

        var deadline = DateTime.UtcNow + searchTimeout;
        while (true)
        {
            if (File.Exists(paths.Value.File))
            {
                var info = new FileInfo(paths.Value.File);
                return new RecoveryCopyInfo(info.LastWriteTimeUtc, info.Length, IsDownloaded: true);
            }

            if (File.Exists(paths.Value.Placeholder))
            {
                return new RecoveryCopyInfo(ModifiedUtcOf(paths.Value.File), null, IsDownloaded: false);
            }

            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> DownloadAsync(TimeSpan timeout, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        var paths = await PathsAsync().ConfigureAwait(false);
        if (paths is null) return null;

        if (File.Exists(paths.Value.File) && IsCurrent(paths.Value.File))
        {
            progress?.Report(1);
            return paths.Value.File;
        }

        var url = NSUrl.FromFilename(paths.Value.File);
        NSFileManager.DefaultManager.StartDownloadingUbiquitous(url, out var error);
        if (error is not null)
        {
            System.Diagnostics.Debug.WriteLine($"iCloud download did not start: {error.LocalizedDescription}");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(paths.Value.File) && IsCurrent(paths.Value.File))
            {
                progress?.Report(1);
                return paths.Value.File;
            }

            progress?.Report(DownloadedFraction(paths.Value.File));
            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task WriteAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var paths = await PathsAsync().ConfigureAwait(false)
                    ?? throw new IOException("The iCloud container is not reachable.");

        Directory.CreateDirectory(Path.GetDirectoryName(paths.File)!);
        var target = NSUrl.FromFilename(paths.File);

        using var coordinator = new NSFileCoordinator();
        coordinator.CoordinateWrite(target, NSFileCoordinatorWritingOptions.ForReplacing, out var error, destination =>
        {
            File.Copy(sourcePath, destination.Path!, overwrite: true);
        });

        if (error is not null)
        {
            throw new IOException(error.LocalizedDescription);
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        var paths = await PathsAsync().ConfigureAwait(false);
        if (paths is null) return;

        var target = NSUrl.FromFilename(paths.Value.File);
        using var coordinator = new NSFileCoordinator();
        coordinator.CoordinateWrite(target, NSFileCoordinatorWritingOptions.ForDeleting, out _, destination =>
        {
            NSFileManager.DefaultManager.Remove(destination, out _);
        });
        // A placeholder has no coordinated identity of its own; plain removal is what is left.
        try
        {
            if (File.Exists(paths.Value.Placeholder)) File.Delete(paths.Value.Placeholder);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static async Task<(string File, string Placeholder)?> PathsAsync()
    {
        var root = await ICloudContainer.RootAsync().ConfigureAwait(false);
        if (root?.Path is null) return null;

        var folder = Path.Combine(root.Path, Folder);
        return (Path.Combine(folder, FileName), Path.Combine(folder, PlaceholderName));
    }

    /// <summary>Whether the local file is the latest the cloud knows, not a stale body awaiting an update.</summary>
    private static bool IsCurrent(string path)
    {
        var url = NSUrl.FromFilename(path);
        if (url.TryGetResource(NSUrl.UbiquitousItemDownloadingStatusKey, out var status) && status is NSString s)
        {
            return s == NSUrl.UbiquitousItemDownloadingStatusCurrent || s == NSUrl.UbiquitousItemDownloadingStatusDownloaded;
        }

        // Not an ubiquitous item as far as the system can tell: a plain file is as current as it gets.
        return true;
    }

    private static double DownloadedFraction(string path)
    {
        var url = NSUrl.FromFilename(path);
        if (url.TryGetResource(NSUrl.UbiquitousItemDownloadingStatusKey, out var status) && status is NSString s
            && s == NSUrl.UbiquitousItemDownloadingStatusDownloaded)
        {
            return 0.9;
        }

        return 0.2;
    }

    private static DateTime? ModifiedUtcOf(string path)
    {
        var url = NSUrl.FromFilename(path);
        if (url.TryGetResource(NSUrl.ContentModificationDateKey, out var value) && value is NSDate date)
        {
            return ((DateTime)date).ToUniversalTime();
        }

        return null;
    }
}
