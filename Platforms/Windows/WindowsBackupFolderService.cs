using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Services.Backup;
using Windows.Storage.Pickers;

namespace Diarion.Services;

/// <summary>
/// The folder picker is WinUI's; everything after it is plain <see cref="System.IO"/>. The app ships
/// unpackaged (<c>WindowsPackageType=None</c>), which means two things at once: there is no package
/// identity, so <c>StorageApplicationPermissions.FutureAccessList</c> throws rather than remembers
/// — and there is no sandbox either, so the path the picker returns can simply be used. The token is
/// the path.
/// </summary>
public sealed class WindowsBackupFolderService : IBackupFolderService
{
    public bool IsSupported => true;

    private static string DevicePath =>
        Directory.CreateDirectory(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Diarion Backups")).FullName;

    /// <summary>Documents\Diarion Backups — the computer's own counterpart of the phone's folder.</summary>
    public BackupFolderPick? DeviceFolder => new(BackupFolderPick.DeviceToken, DevicePath);

    /// <summary>A picked folder's token is its path; the device token stands for <see cref="DevicePath"/>.</summary>
    private static string PathOf(string token) => token == BackupFolderPick.DeviceToken ? DevicePath : token;

    public async Task<BackupFolderPick?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");

        // WinUI pickers need the owning window, and there is no implicit one for a desktop process.
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (window is null) return null;

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();

        // A library or other virtual location has no path, and without identity there is no other
        // handle to keep. Treated as "nothing picked"; the switch goes back and the user can pick a
        // real folder instead.
        if (folder is null || string.IsNullOrEmpty(folder.Path)) return null;

        return new BackupFolderPick(folder.Path, folder.Path);
    }

    public Task<bool> CanWriteAsync(string token) =>
        Task.FromResult(!string.IsNullOrEmpty(token) && Directory.Exists(PathOf(token)));

    public async Task WriteFileAsync(string token, string fileName, string sourcePath)
    {
        token = PathOf(token);
        if (!Directory.Exists(token))
        {
            throw new IOException("The backup folder is no longer reachable.");
        }

        var destination = Path.Combine(token, fileName);
        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target);
    }

    public Task<IReadOnlyList<string>> ListFileNamesAsync(string token)
    {
        token = PathOf(token);
        if (!Directory.Exists(token))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        IReadOnlyList<string> names = Directory.EnumerateFiles(token)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
        return Task.FromResult(names);
    }

    public Task DeleteFileAsync(string token, string fileName)
    {
        var path = Path.Combine(PathOf(token), fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
