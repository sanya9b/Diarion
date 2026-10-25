using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diarion.Services.Backup;

/// <summary>The answer on a platform with no folder provider wired up: the feature is not offered.</summary>
public sealed class NullBackupFolderService : IBackupFolderService
{
    public bool IsSupported => false;

    public BackupFolderPick? DeviceFolder => null;

    public Task<BackupFolderPick?> PickFolderAsync() => Task.FromResult<BackupFolderPick?>(null);

    public Task<bool> CanWriteAsync(string token) => Task.FromResult(false);

    public Task WriteFileAsync(string token, string fileName, string sourcePath) =>
        throw new NotSupportedException("Scheduled backups are not supported on this platform.");

    public Task<IReadOnlyList<string>> ListFileNamesAsync(string token) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task DeleteFileAsync(string token, string fileName) => Task.CompletedTask;
}
