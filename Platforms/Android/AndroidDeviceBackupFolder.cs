using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace Diarion.Services;

/// <summary>
/// The phone's own folder for automatic copies (owner, 2026-10-23: "хочу щоб в телефон бекап
/// автоматично зберігався"), with no picker and no permission.
/// </summary>
/// <remarks>
/// <para>
/// Android 10 and later: Download/Diarion through MediaStore. An app may add, list and delete its own
/// files there without any storage permission, so the manifest's allowlist stays at nine. The folder
/// is visible in the Files app and survives the app being uninstalled, which is the case a copy is
/// for. The one catch is ownership: after a reinstall the old copies belong to the previous install,
/// so they are not listed and not pruned — they stay, which errs the safe way.
/// </para>
/// <para>
/// Android 8 and 9 would need WRITE_EXTERNAL_STORAGE for the shared folder, which is not on the
/// allowlist. There the copies go to the app's own folder on external storage
/// (Android/data/org.diarion.app/files/Backups): no permission, reachable over USB, but removed with
/// the app — the settings card names the path so nobody mistakes it for the shared one.
/// </para>
/// </remarks>
internal static class AndroidDeviceBackupFolder
{
    private const string Mime = "application/octet-stream";

    // MediaStore column names, spelled out: the binding's constant names differ across versions, the
    // strings themselves are part of the platform contract.
    private const string ColumnId = "_id";
    private const string ColumnDisplayName = "_display_name";
    private const string ColumnRelativePath = "relative_path";
    private const string ColumnMimeType = "mime_type";
    private const string ColumnIsPending = "is_pending";

    private static readonly string RelativePath = Android.OS.Environment.DirectoryDownloads + "/Diarion/";

    private static ContentResolver Resolver =>
        Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("No content resolver.");

    private static bool UsesMediaStore => OperatingSystem.IsAndroidVersionAtLeast(29);

    public static string DisplayName => UsesMediaStore
        ? Diarion.Resources.Localization.AppResources.AutoBackupDeviceFolderAndroid
        : AppFolder().FullName;

    public static Task<bool> CanWriteAsync()
    {
        if (UsesMediaStore) return Task.FromResult(true);

        var folder = AppFolder();
        return Task.FromResult(folder.Exists);
    }

    public static async Task WriteFileAsync(string fileName, string sourcePath)
    {
        if (!UsesMediaStore)
        {
            var target = Path.Combine(AppFolder().FullName, fileName);
            await using var input = File.OpenRead(sourcePath);
            await using var output = File.Create(target);
            await input.CopyToAsync(output);
            return;
        }

        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return;

        var resolver = Resolver;

        // Insert renames on a clash ("name (1)") rather than replacing, so a same-named copy goes first.
        DeleteMediaStoreFile(resolver, fileName);

        var values = new ContentValues();
        values.Put(ColumnDisplayName, fileName);
        values.Put(ColumnMimeType, Mime);
        values.Put(ColumnRelativePath, RelativePath);
        // Pending while it is written, so a half-written copy is never visible as a finished one.
        values.Put(ColumnIsPending, 1);

        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values)
                  ?? throw new IOException("The Downloads folder did not accept a new file.");
        try
        {
            await using (var output = resolver.OpenOutputStream(uri, "w")
                                      ?? throw new IOException("The new copy could not be opened for writing."))
            await using (var input = File.OpenRead(sourcePath))
            {
                await input.CopyToAsync(output);
                await output.FlushAsync();
            }

            var done = new ContentValues();
            done.Put(ColumnIsPending, 0);
            resolver.Update(uri, done, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }

    public static Task<IReadOnlyList<string>> ListFileNamesAsync()
    {
        if (!UsesMediaStore)
        {
            IReadOnlyList<string> local = AppFolder().EnumerateFiles().Select(f => f.Name).ToList();
            return Task.FromResult(local);
        }

        IReadOnlyList<string> names = OperatingSystem.IsAndroidVersionAtLeast(29)
            ? MediaStoreFiles(Resolver).Select(f => f.Name).ToList()
            : Array.Empty<string>();
        return Task.FromResult(names);
    }

    public static Task DeleteFileAsync(string fileName)
    {
        if (!UsesMediaStore)
        {
            var path = Path.Combine(AppFolder().FullName, fileName);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            DeleteMediaStoreFile(Resolver, fileName);
        }

        return Task.CompletedTask;
    }

    private static DirectoryInfo AppFolder()
    {
        var root = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                   ?? Android.App.Application.Context.FilesDir?.AbsolutePath
                   ?? throw new InvalidOperationException("No app storage.");
        return Directory.CreateDirectory(Path.Combine(root, "Backups"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static void DeleteMediaStoreFile(ContentResolver resolver, string fileName)
    {
        foreach (var (id, name) in MediaStoreFiles(resolver))
        {
            if (!string.Equals(name, fileName, StringComparison.Ordinal)) continue;

            var uri = ContentUris.WithAppendedId(MediaStore.Downloads.ExternalContentUri, id);
            resolver.Delete(uri, null, null);
        }
    }

    /// <summary>This app's own files in Download/Diarion — the only ones MediaStore shows it without a permission.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static List<(long Id, string Name)> MediaStoreFiles(ContentResolver resolver)
    {
        var files = new List<(long, string)>();
        using var cursor = resolver.Query(
            MediaStore.Downloads.ExternalContentUri,
            new[] { ColumnId, ColumnDisplayName },
            ColumnRelativePath + " = ?",
            new[] { RelativePath },
            null);
        if (cursor is null) return files;

        while (cursor.MoveToNext())
        {
            var name = cursor.GetString(1);
            if (name is not null) files.Add((cursor.GetLong(0), name));
        }

        return files;
    }
}
