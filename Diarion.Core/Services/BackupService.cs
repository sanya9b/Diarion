using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Services.Database;

namespace Diarion.Services;

public class BackupService : IBackupService
{
    private const string BackupFilePrefix = "DiarionBackup_";
    private const string LegacyBackupExtension = ".db";

    private readonly IDatabaseContext _dbContext;
    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly IFileSystemService _fileSystem;
    private readonly IShareService _shareService;
    private readonly IFilePickerService _filePicker;

    public BackupService(
        IDatabaseContext dbContext,
        IEncryptionKeyProvider keyProvider,
        IFileSystemService fileSystem,
        IShareService shareService,
        IFilePickerService filePicker)
    {
        _dbContext = dbContext;
        _keyProvider = keyProvider;
        _fileSystem = fileSystem;
        _shareService = shareService;
        _filePicker = filePicker;
    }

    public async Task<BackupOutcome> ExportBackupAsync(Func<Task<string?>> passphraseProvider)
    {
        try
        {
            var dbPath = _dbContext.DatabasePath;
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                return BackupOutcome.Failed;
            }

            var passphrase = await passphraseProvider();
            if (string.IsNullOrEmpty(passphrase))
            {
                return BackupOutcome.Cancelled;
            }

            CleanupOldTempBackups();

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outputPath = Path.Combine(
                _fileSystem.CacheDirectory,
                $"{BackupFilePrefix}{stamp}{PortableBackupFile.FileExtension}");

            var written = await WritePortableBackupAsync(passphrase, outputPath);
            if (written != BackupOutcome.Success)
            {
                return written;
            }

            await _shareService.ShareFileAsync("Diarion Backup", outputPath);
            return BackupOutcome.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export Backup Error: {ex.Message}");
            return BackupOutcome.Failed;
        }
    }

    public Task<BackupOutcome> WritePortableBackupAsync(string passphrase, string destinationPath)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return Task.FromResult(BackupOutcome.Cancelled);
        }

        // Off the calling thread: re-encrypting the database is seconds of CPU on a large diary, and
        // the scheduled caller runs this from a launch hook while the home screen is still drawing.
        return Task.Run(() => WritePortableBackup(passphrase, destinationPath));
    }

    private BackupOutcome WritePortableBackup(string passphrase, string destinationPath)
    {
        var reencryptedPath = destinationPath + ".reencrypt.tmp";
        try
        {
            var dbPath = _dbContext.DatabasePath;
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                return BackupOutcome.Failed;
            }

            var salt = BackupKeyDeriver.NewSalt();
            var iterations = BackupKeyDeriver.CurrentIterations;
            var backupKey = BackupKeyDeriver.DeriveKey(passphrase, salt, iterations);

            // Through the live connection: the database is never closed for a copy. The previous
            // version closed it for the seconds the re-encryption took, and any screen that loaded in
            // that window threw out of a closed database (and, on iOS, took the app down with it).
            _dbContext.ExportTo(reencryptedPath, backupKey);

            using (var payload = File.OpenRead(reencryptedPath))
            using (var output = File.Create(destinationPath))
            {
                PortableBackupFile.Write(output, salt, iterations, payload);
            }

            return BackupOutcome.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Portable backup write error: {ex.Message}");
            SafeDelete(destinationPath);
            return BackupOutcome.Failed;
        }
        finally
        {
            // The intermediate carries the same data as the envelope and must not linger.
            SafeDelete(reencryptedPath);
        }
    }

    public async Task<BackupOutcome> ImportBackupAsync(Func<Task<string?>> passphraseProvider)
    {
        string? tempImportPath = null;
        try
        {
            using var picked = await _filePicker.PickBackupFileAsync("Select Backup File");
            if (picked == null)
            {
                return BackupOutcome.Cancelled;
            }

            var dbPath = _dbContext.DatabasePath;
            if (string.IsNullOrEmpty(dbPath))
            {
                return BackupOutcome.Failed;
            }

            // Land the picked file next to the live DB so the later swap stays on one volume and is atomic.
            var dbDir = Path.GetDirectoryName(dbPath)!;
            tempImportPath = Path.Combine(dbDir, $"import_{Guid.NewGuid():N}.tmp");
            using (var dest = File.Create(tempImportPath))
            {
                await picked.CopyToAsync(dest);
            }

            var (candidatePath, prepared) = await PrepareCandidateAsync(tempImportPath, dbDir, passphraseProvider);
            if (prepared != BackupOutcome.Success)
            {
                return prepared;
            }

            BackupOutcome swapped;
            try
            {
                // Off the UI thread like the preparation: opening the candidate and swapping the files
                // is quick, but the reopen behind the swap runs the migrations and the seeder.
                swapped = await Task.Run(() =>
                {
                    if (!EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(
                            candidatePath!,
                            _keyProvider.GetOrCreateKey(),
                            DatabaseConstants.EntriesCollection,
                            MigrationRunner.CurrentVersion))
                    {
                        // The candidate opened during preparation, so a failure here is the version gate
                        // rather than the key — anything else would already have been reported above.
                        return BackupOutcome.NewerSchema;
                    }

                    _dbContext.ReplaceDatabaseFile(candidatePath!);
                    return BackupOutcome.Success;
                });
            }
            finally
            {
                if (!string.Equals(candidatePath, tempImportPath, StringComparison.Ordinal))
                {
                    SafeDelete(candidatePath);
                }
            }

            if (swapped == BackupOutcome.Success)
            {
                WeakReferenceMessenger.Default.Send(new DatabaseRestoredMessage());
            }
            return swapped;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import Backup Error: {ex.Message}");
            return BackupOutcome.Failed;
        }
        finally
        {
            if (tempImportPath != null)
            {
                SafeDelete(tempImportPath);
            }
        }
    }

    /// <summary>
    /// Turns whatever the user picked into a database file encrypted with THIS device's key, ready to
    /// be swapped in. A portable backup is unwrapped and re-encrypted; a legacy backup is accepted as
    /// it stands, but only if this device's key still opens it.
    /// </summary>
    private async Task<(string? Path, BackupOutcome Outcome)> PrepareCandidateAsync(
        string pickedPath, string workingDirectory, Func<Task<string?>> passphraseProvider)
    {
        var deviceKey = _keyProvider.GetOrCreateKey();

        PortableBackupFile.Header? header;
        using (var probe = File.OpenRead(pickedPath))
        {
            header = PortableBackupFile.TryReadHeader(probe);
        }

        if (header == null)
        {
            // No envelope: either a pre-portable backup from this very device, or not ours at all.
            return await Task.Run(() => EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(
                pickedPath, deviceKey, DatabaseConstants.EntriesCollection)
                ? (pickedPath, BackupOutcome.Success)
                : (null, LooksLikeLegacyBackup(pickedPath)
                    ? BackupOutcome.LegacyBackupFromAnotherDevice
                    : BackupOutcome.NotADiarionBackup));
        }

        var passphrase = await passphraseProvider();
        if (string.IsNullOrEmpty(passphrase))
        {
            return (null, BackupOutcome.Cancelled);
        }

        // Everything from here is CPU: 600k rounds of PBKDF2, then the whole diary decrypted and
        // encrypted again. On the UI thread that froze the screen for seconds on a large diary.
        return await Task.Run(() => PrepareFromEnvelope(pickedPath, workingDirectory, header, passphrase, deviceKey));
    }

    private static (string? Path, BackupOutcome Outcome) PrepareFromEnvelope(
        string pickedPath, string workingDirectory, PortableBackupFile.Header header, string passphrase, string deviceKey)
    {
        var backupKey = BackupKeyDeriver.DeriveKey(passphrase, header.Salt, header.Iterations);

        var payloadPath = Path.Combine(workingDirectory, $"payload_{Guid.NewGuid():N}.tmp");
        var candidatePath = Path.Combine(workingDirectory, $"candidate_{Guid.NewGuid():N}.tmp");

        try
        {
            using (var source = File.OpenRead(pickedPath))
            using (var payload = File.Create(payloadPath))
            {
                PortableBackupFile.ExtractPayload(source, header, payload);
            }

            if (!EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(
                    payloadPath, backupKey, DatabaseConstants.EntriesCollection))
            {
                // The envelope was ours, so the file is a Diarion backup; the key is what did not fit.
                SafeDelete(candidatePath);
                return (null, BackupOutcome.WrongPassphrase);
            }

            // Re-key onto this device so the restored database opens on every subsequent launch.
            EncryptedLiteDatabaseFactory.ReencryptTo(payloadPath, backupKey, candidatePath, deviceKey);
            return (candidatePath, BackupOutcome.Success);
        }
        catch
        {
            SafeDelete(candidatePath);
            throw;
        }
        finally
        {
            SafeDelete(payloadPath);
        }
    }

    /// <summary>
    /// Whether an unopenable file is nonetheless a LiteDB database — which tells the user their backup
    /// is fine but belongs to a device whose key is gone, rather than that they picked the wrong file.
    /// </summary>
    private static bool LooksLikeLegacyBackup(string path)
    {
        try
        {
            return string.Equals(Path.GetExtension(path), LegacyBackupExtension, StringComparison.OrdinalIgnoreCase)
                   || new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupOldTempBackups()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_fileSystem.CacheDirectory, $"{BackupFilePrefix}*"))
            {
                SafeDelete(file);
            }
        }
        catch
        {
            // Best-effort hygiene.
        }
    }

    private static void SafeDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
