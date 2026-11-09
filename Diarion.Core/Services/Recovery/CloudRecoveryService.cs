using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Services.Database;

namespace Diarion.Services.Recovery;

/// <inheritdoc />
public sealed class CloudRecoveryService : ICloudRecoveryService
{
    /// <summary>How long a restore waits for iCloud to hand over the file before giving up.</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(90);

    private readonly IRecoveryStore _store;
    private readonly ICloudRecoverySettingsStore _settingsStore;
    private readonly IDatabaseContext _database;
    private readonly IEncryptionKeyProvider _keys;
    private readonly IFileSystemService _fileSystem;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CloudRecoverySettings _settings;
    private bool _changedSinceCopy;
    private bool _started;

    public CloudRecoveryService(
        IRecoveryStore store,
        ICloudRecoverySettingsStore settingsStore,
        IDatabaseContext database,
        IEncryptionKeyProvider keys,
        IFileSystemService fileSystem,
        TimeProvider? clock = null)
    {
        _store = store;
        _settingsStore = settingsStore;
        _database = database;
        _keys = keys;
        _fileSystem = fileSystem;
        _clock = clock ?? TimeProvider.System;
        _settings = settingsStore.Load();
    }

    public bool IsSupported => _store.IsSupported;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _store.IsSupported ? _store.IsAvailableAsync(cancellationToken) : Task.FromResult(false);

    public CloudRecoverySettings Settings => _settings;

    public bool NeedsDecision => IsSupported && _settings.Decision == CloudRecoveryDecision.Undecided;

    /// <summary>
    /// Follows the saves so the background flush knows whether there is anything new to copy.
    /// Idempotent, like the other singletons the window creation wires up.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<CloudRecoveryService, DocumentChangedMessage>(this, static (self, _) => self.MarkChanged());
        messenger.Register<CloudRecoveryService, TodoStoreChangedMessage>(this, static (self, _) => self.MarkChanged());
    }

    public async Task<CloudRecoveryOutcome> EnableAsync(CancellationToken cancellationToken = default)
    {
        _settings.Decision = CloudRecoveryDecision.Enabled;
        _settings.LastOutcome = CloudRecoveryOutcome.None;
        _settingsStore.Save(_settings);
        return await CopyNowAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Decline()
    {
        _settings.Decision = CloudRecoveryDecision.Disabled;
        _settingsStore.Save(_settings);
    }

    public async Task DisableAsync(bool deleteCopy, CancellationToken cancellationToken = default)
    {
        _settings.Decision = CloudRecoveryDecision.Disabled;
        _settingsStore.Save(_settings);
        if (deleteCopy)
        {
            await DeleteCopyAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<CloudRecoveryOutcome> CopyNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsEnabled) return Task.FromResult(CloudRecoveryOutcome.Disabled);
        return CopyAsync(cancellationToken);
    }

    public Task<CloudRecoveryOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsEnabled) return Task.FromResult(CloudRecoveryOutcome.Disabled);
        if (!IsDue(CloudRecoverySettings.LaunchInterval)) return Task.FromResult(CloudRecoveryOutcome.NotDue);
        return CopyAsync(cancellationToken);
    }

    public void MarkChanged() => _changedSinceCopy = true;

    public Task<CloudRecoveryOutcome> FlushOnBackgroundAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsEnabled) return Task.FromResult(CloudRecoveryOutcome.Disabled);
        if (!_changedSinceCopy || !IsDue(CloudRecoverySettings.BackgroundInterval)) return Task.FromResult(CloudRecoveryOutcome.NotDue);
        return CopyAsync(cancellationToken);
    }

    public Task<RecoveryCopyInfo?> FindCopyAsync(TimeSpan searchTimeout, CancellationToken cancellationToken = default) =>
        IsSupported ? _store.FindAsync(searchTimeout, cancellationToken) : Task.FromResult<RecoveryCopyInfo?>(null);

    public async Task<RecoveryRestoreOutcome> RestoreAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsSupported) return RecoveryRestoreOutcome.NotFound;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var found = await _store.FindAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (found is null) return RecoveryRestoreOutcome.NotFound;

            var path = await _store.DownloadAsync(DownloadTimeout, progress, cancellationToken).ConfigureAwait(false);
            if (path is null) return RecoveryRestoreOutcome.DownloadFailed;

            var key = _keys.GetOrCreateKey();
            if (!EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(path, key, DatabaseConstants.EntriesCollection))
            {
                return RecoveryRestoreOutcome.KeyMismatch;
            }

            if (!EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(path, key, DatabaseConstants.EntriesCollection, MigrationRunner.CurrentVersion))
            {
                return RecoveryRestoreOutcome.NewerSchema;
            }

            // Next to the live file, so the swap is a rename on one volume.
            var directory = Path.GetDirectoryName(_database.DatabasePath) ?? _fileSystem.AppDataDirectory;
            var candidate = Path.Combine(directory, $"recovery_{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(path, candidate, overwrite: true);
                _database.ReplaceDatabaseFile(candidate);
            }
            finally
            {
                SafeDelete(candidate);
            }

            _settings.Decision = CloudRecoveryDecision.Enabled;
            _settings.LastCopyUtc = found.ModifiedUtc ?? _settings.LastCopyUtc;
            _settings.LastOutcome = CloudRecoveryOutcome.Success;
            _settingsStore.Save(_settings);
            _changedSinceCopy = false;

            WeakReferenceMessenger.Default.Send(new DatabaseRestoredMessage());
            return RecoveryRestoreOutcome.Restored;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud restore failed: {ex}");
            return RecoveryRestoreOutcome.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCopyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.DeleteAsync(cancellationToken).ConfigureAwait(false);
            _settings.LastCopyUtc = null;
            _settings.LastOutcome = CloudRecoveryOutcome.None;
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud copy delete failed: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsDue(TimeSpan interval) =>
        _settings.LastCopyUtc is not DateTime last || _clock.GetUtcNow().UtcDateTime - last >= interval;

    private async Task<CloudRecoveryOutcome> CopyAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported) return Record(CloudRecoveryOutcome.Unavailable);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await _store.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                return Record(CloudRecoveryOutcome.Unavailable);
            }

            var temp = Path.Combine(_fileSystem.CacheDirectory, $"recovery_{Guid.NewGuid():N}.tmp");
            try
            {
                // The same key as the live database: the copy is for this phone, or for one that
                // shares its keychain. Through the live connection, so nothing closes meanwhile.
                _database.ExportTo(temp, _keys.GetOrCreateKey());
                cancellationToken.ThrowIfCancellationRequested();
                await _store.WriteAsync(temp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                SafeDelete(temp);
            }

            _settings.LastCopyUtc = _clock.GetUtcNow().UtcDateTime;
            _changedSinceCopy = false;
            return Record(CloudRecoveryOutcome.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud copy failed: {ex}");
            return Record(CloudRecoveryOutcome.Failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CloudRecoveryOutcome Record(CloudRecoveryOutcome outcome)
    {
        _settings.LastOutcome = outcome;
        _settingsStore.Save(_settings);
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
            // A temp file in the cache directory; best-effort.
        }
    }
}
