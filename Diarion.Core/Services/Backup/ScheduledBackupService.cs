using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services.Database;

namespace Diarion.Services.Backup;

/// <summary>
/// The scheduler. Decides whether a backup is due, has <see cref="IBackupService"/> write the same
/// portable envelope the manual export writes, hands the file to the folder provider, and keeps the
/// folder from filling up.
/// </summary>
/// <remarks>
/// <para>
/// The file is the manual export's file, byte for byte in format: a <c>.diarionbackup</c> envelope
/// whose key is derived from the passphrase. Restoring one goes through the ordinary "Restore"
/// button, which asks for the passphrase. There is deliberately no second format and no second
/// restore path.
/// </para>
/// <para>
/// Only success moves the clock. A failed attempt — the folder gone, the drive unmounted — leaves
/// <see cref="ScheduledBackupSettings.LastRunUtc"/> where it was, so the next launch tries again
/// rather than waiting out another week on top of a failure, and the outcome is kept so the settings
/// screen can say what happened.
/// </para>
/// </remarks>
public sealed class ScheduledBackupService : IScheduledBackupService
{
    /// <summary>Shared with the manual export, so pruning recognises both.</summary>
    public const string FilePrefix = "DiarionBackup_";

    private readonly IBackupService _backup;
    private readonly IBackupFolderService _folder;
    private readonly IScheduledBackupStore _store;
    private readonly IFileSystemService _fileSystem;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ScheduledBackupSettings _settings;

    public ScheduledBackupService(
        IBackupService backup,
        IBackupFolderService folder,
        IScheduledBackupStore store,
        IFileSystemService fileSystem,
        TimeProvider? clock = null)
    {
        _backup = backup;
        _folder = folder;
        _store = store;
        _fileSystem = fileSystem;
        _clock = clock ?? TimeProvider.System;
        _settings = store.Load();
    }

    public bool IsSupported => _folder.IsSupported;

    public ScheduledBackupSettings Settings => _settings;

    public bool IsDue(DateTime nowUtc)
    {
        if (!_settings.IsEnabled || string.IsNullOrEmpty(_settings.FolderToken)) return false;
        if (_settings.LastRunUtc is not DateTime last) return true;

        return nowUtc - last >= TimeSpan.FromDays(ScheduledBackupSettings.NormalizeInterval(_settings.IntervalDays));
    }

    public Task<ScheduledBackupOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsEnabled || string.IsNullOrEmpty(_settings.FolderToken))
        {
            return Task.FromResult(ScheduledBackupOutcome.Disabled);
        }

        return IsDue(_clock.GetUtcNow().UtcDateTime)
            ? RunNowAsync(cancellationToken)
            : Task.FromResult(ScheduledBackupOutcome.NotDue);
    }

    public async Task<ScheduledBackupOutcome> RunNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsEnabled || string.IsNullOrEmpty(_settings.FolderToken))
        {
            return ScheduledBackupOutcome.Disabled;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = _settings.FolderToken!;

            var passphrase = _store.GetPassphrase();
            if (string.IsNullOrEmpty(passphrase))
            {
                return Record(ScheduledBackupOutcome.NoPassphrase);
            }

            if (!await SafeCanWriteAsync(token))
            {
                return Record(ScheduledBackupOutcome.FolderUnavailable);
            }

            var tempPath = Path.Combine(_fileSystem.CacheDirectory, $"scheduled_{Guid.NewGuid():N}.tmp");
            try
            {
                var written = await _backup.WritePortableBackupAsync(passphrase, tempPath);
                if (written != BackupOutcome.Success)
                {
                    return Record(ScheduledBackupOutcome.Failed);
                }

                cancellationToken.ThrowIfCancellationRequested();

                var now = _clock.GetUtcNow();
                await _folder.WriteFileAsync(token, FileNameFor(now.ToLocalTime().DateTime), tempPath);
                await PruneAsync(token, _settings.KeepCount);

                _settings.LastRunUtc = now.UtcDateTime;
                return Record(ScheduledBackupOutcome.Success);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scheduled backup failed: {ex.Message}");
                return Record(ScheduledBackupOutcome.Failed);
            }
            finally
            {
                SafeDelete(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BackupFolderPick?> PickFolderAsync() => _folder.PickFolderAsync();

    public void Enable(BackupFolderPick folder, string passphrase, int intervalDays)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new ArgumentException("A passphrase is required.", nameof(passphrase));
        }

        _store.SetPassphrase(passphrase);
        _settings.IsEnabled = true;
        _settings.FolderToken = folder.Token;
        _settings.FolderName = folder.DisplayName;
        _settings.IntervalDays = ScheduledBackupSettings.NormalizeInterval(intervalDays);
        _settings.LastOutcome = ScheduledBackupOutcome.None;
        _store.Save(_settings);
    }

    public void Disable()
    {
        _settings.IsEnabled = false;
        // A secret with nothing to protect should not linger; the folder choice is harmless and
        // saves a second trip through the picker when the switch comes back on.
        _store.SetPassphrase(null);
        _store.Save(_settings);
    }

    public void SetInterval(int days)
    {
        _settings.IntervalDays = ScheduledBackupSettings.NormalizeInterval(days);
        _store.Save(_settings);
    }

    public void SetFolder(BackupFolderPick folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _settings.FolderToken = folder.Token;
        _settings.FolderName = folder.DisplayName;
        _store.Save(_settings);
    }

    /// <summary>Same shape as the manual export's name, so the two sort together in the folder.</summary>
    public static string FileNameFor(DateTime localTime) =>
        $"{FilePrefix}{localTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}{PortableBackupFile.FileExtension}";

    public static bool IsOurs(string fileName) =>
        fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
        && fileName.EndsWith(PortableBackupFile.FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Leaves the newest <paramref name="keep"/> of the app's own files and deletes the rest. Only
    /// files that match the app's own naming are touched; anything else in the folder is the user's.
    /// Newest by name, which is newest by time — the name is a timestamp — and does not depend on
    /// file metadata a cloud provider may or may not preserve.
    /// </summary>
    private async Task PruneAsync(string token, int keep)
    {
        if (keep < 1) return;

        var names = await _folder.ListFileNamesAsync(token);
        var stale = names
            .Where(IsOurs)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        foreach (var name in stale)
        {
            try
            {
                await _folder.DeleteFileAsync(token, name);
            }
            catch (Exception ex)
            {
                // An old copy that will not delete is clutter, not a failed backup.
                System.Diagnostics.Debug.WriteLine($"Could not prune {name}: {ex.Message}");
            }
        }
    }

    private async Task<bool> SafeCanWriteAsync(string token)
    {
        try
        {
            return await _folder.CanWriteAsync(token);
        }
        catch
        {
            return false;
        }
    }

    private ScheduledBackupOutcome Record(ScheduledBackupOutcome outcome)
    {
        _settings.LastOutcome = outcome;
        _store.Save(_settings);
        return outcome;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of a temp file in the cache directory.
        }
    }
}
