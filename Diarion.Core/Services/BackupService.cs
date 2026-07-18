using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Diarion.Services.Database;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

public class BackupService : IBackupService
{
    private const string BackupFilePrefix = "DiarionBackup_";
    private readonly IDatabaseContext _dbContext;
    private readonly IEncryptionKeyProvider _keyProvider;

    public BackupService(IDatabaseContext dbContext, IEncryptionKeyProvider keyProvider)
    {
        _dbContext = dbContext;
        _keyProvider = keyProvider;
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
            var tempFile = Path.Combine(FileSystem.CacheDirectory, $"{BackupFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.db");
            _dbContext.Close();
            try
            {
                File.Copy(dbPath, tempFile, overwrite: true);
            }
            finally
            {
                _dbContext.Reopen();
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Diarion Backup",
                File = new ShareFile(tempFile)
            });

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
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Backup File",
                FileTypes = BackupFileType()
            });

            if (result == null)
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
            using (var source = await result.OpenReadAsync())
            using (var dest = File.Create(tempImportPath))
            {
                await source.CopyToAsync(dest);
            }

            // 2. Validate: it must be a real Diarion database that opens with THIS device's key.
            //    Rejects foreign/corrupt files and, by design, backups from another device.
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

    private static FilePickerFileType BackupFileType() =>
        new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.data", "public.database" } },
            { DevicePlatform.Android, new[] { "application/octet-stream" } },
            { DevicePlatform.WinUI, new[] { ".db" } },
            { DevicePlatform.macOS, new[] { "public.data", "public.database" } }
        });

    private static void CleanupOldTempBackups()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(FileSystem.CacheDirectory, $"{BackupFilePrefix}*.db"))
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
