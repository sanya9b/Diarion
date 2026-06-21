using System;
using System.IO;
using System.Threading.Tasks;
using Diarion.Services.Database;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

public class BackupService : IBackupService
{
    private readonly IDatabaseContext _dbContext;

    public BackupService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
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

            // LiteDB creates a lock, but we can copy the file.
            // A safer way is to copy it to a temp file first.
            var tempFile = Path.Combine(FileSystem.CacheDirectory, $"DiarionBackup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(dbPath, tempFile, overwrite: true);

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
        try
        {
            var customFileType = new FilePickerFileType(
                new System.Collections.Generic.Dictionary<DevicePlatform, System.Collections.Generic.IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.data", "public.database" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream", "application/x-sqlite3" } },
                    { DevicePlatform.WinUI, new[] { ".db" } },
                    { DevicePlatform.macOS, new[] { "public.data", "public.database" } }
                });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Backup File",
                FileTypes = customFileType
            });

            if (result != null)
            {
                var dbPath = _dbContext.DatabasePath;
                if (string.IsNullOrEmpty(dbPath)) return false;

                using var stream = await result.OpenReadAsync();
                
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var bytes = memoryStream.ToArray();
                
                // Close DB connection to release lock
                _dbContext.Close();
                
                File.WriteAllBytes(dbPath, bytes);
                
                // Note: Next call to GetCollection will automatically re-open the DB
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import Backup Error: {ex.Message}");
            return false;
        }
    }
}