using System;
using System.IO;
using System.Threading.Tasks;
using Diarion.Services.Database;

namespace Diarion.Services;

public class BackupService : IBackupService
{
    private const string BackupFilePrefix = "DiarionBackup_";
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

    public async Task<bool> ExportBackupAsync()
    {
        try
        {
            var dbPath = _dbContext.DatabasePath;
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                return false;
            }

            CleanupOldTempBackups();

            // Close the DB so the on-disk file is fully checkpointed and unlocked before copying,
            // then reopen. The copied file is already AES-encrypted (encryption at rest), so the
            // backup never contains plaintext.
            var tempFile = Path.Combine(_fileSystem.CacheDirectory, $"{BackupFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.db");
            _dbContext.Close();
            try
            {
                File.Copy(dbPath, tempFile, overwrite: true);
            }
            finally
            {
                _dbContext.Reopen();
            }

            await _shareService.ShareFileAsync("Diarion Backup", tempFile);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export Backup Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ImportBackupAsync()
    {
        string? tempImportPath = null;
        try
        {
            using var picked = await _filePicker.PickBackupFileAsync("Select Backup File");
            if (picked == null)
            {
                return false;
            }

            var dbPath = _dbContext.DatabasePath;
            if (string.IsNullOrEmpty(dbPath))
            {
                return false;
            }

            // 1. Copy the picked file to a temp file NEXT TO the live DB (same volume, so the
            //    later File.Replace/Move is atomic and cross-volume-safe) — never over the live DB yet.
            var dbDir = Path.GetDirectoryName(dbPath)!;
            tempImportPath = Path.Combine(dbDir, $"import_{Guid.NewGuid():N}.tmp");
            using (var dest = File.Create(tempImportPath))
            {
                await picked.CopyToAsync(dest);
            }

            // 2. Validate: it must be a real Diarion database that opens with THIS device's key and
            //    is not from a newer schema. Rejects foreign/corrupt/wrong-device/newer files.
            var password = _keyProvider.GetOrCreateKey();
            if (!EncryptedLiteDatabaseFactory.IsValidEncryptedDatabase(tempImportPath, password, DatabaseConstants.EntriesCollection, MigrationRunner.CurrentVersion))
            {
                return false; // live DB untouched
            }

            // 3. Atomically swap the validated file in, keeping the old DB as a backup, then reopen.
            var rollbackPath = dbPath + ".importbak";
            SafeDelete(rollbackPath);
            _dbContext.Close();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Replace(tempImportPath, dbPath, rollbackPath);
                }
                else
                {
                    File.Move(tempImportPath, dbPath);
                }
                tempImportPath = null; // consumed by the swap
            }
            catch
            {
                // Restore the original if the swap left the live DB missing.
                if (!File.Exists(dbPath) && File.Exists(rollbackPath))
                {
                    File.Copy(rollbackPath, dbPath, overwrite: true);
                }
                throw;
            }
            finally
            {
                _dbContext.Reopen();
            }

            SafeDelete(rollbackPath);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import Backup Error: {ex.Message}");
            return false;
        }
        finally
        {
            if (tempImportPath != null)
            {
                SafeDelete(tempImportPath);
            }
        }
    }

    private void CleanupOldTempBackups()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_fileSystem.CacheDirectory, $"{BackupFilePrefix}*.db"))
            {
                SafeDelete(file);
            }
        }
        catch
        {
            // Best-effort hygiene.
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
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
