using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Services.Backup;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using UIKit;
using UniformTypeIdentifiers;

namespace Diarion.Services;

/// <summary>
/// A folder outside the sandbox — in iCloud Drive, in a third-party provider, "On My iPhone" —
/// reached again later through a security-scoped bookmark. The bookmark is the only thing iOS lets
/// an app keep: a path is valid for one access session and no longer.
/// </summary>
/// <remarks>
/// Writes go through <see cref="NSFileCoordinator"/>, which is how a file provider learns that a
/// document changed under it; iCloud Drive in particular uploads coordinated writes promptly and
/// uncoordinated ones whenever it notices. Every access is bracketed by start/stop of the
/// security scope, including the one that creates the bookmark in the first place.
/// </remarks>
public sealed class IosBackupFolderService : IBackupFolderService
{
    public bool IsSupported => true;

    /// <summary>
    /// Documents/Backups inside the app: Files → On My iPhone → Diarion (Info.plist turns on file
    /// sharing), and part of the iPhone's own iCloud backup. No picker, no bookmark, no security scope.
    /// </summary>
    public BackupFolderPick? DeviceFolder =>
        new(BackupFolderPick.DeviceToken, Diarion.Resources.Localization.AppResources.AutoBackupDeviceFolderIos);

    public async Task<BackupFolderPick?> PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<NSUrl?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var host = Platform.GetCurrentUIViewController();
            if (host is null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder }, asCopy: false)
            {
                AllowsMultipleSelection = false,
                Delegate = new PickerDelegate(tcs)
            };

            host.PresentViewController(picker, true, null);
        });

        var url = await tcs.Task;
        if (url is null) return null;

        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            var bookmark = url.CreateBookmarkData(NSUrlBookmarkCreationOptions.MinimalBookmark, null, null, out var error);
            if (bookmark is null || error is not null)
            {
                throw new IOException(error?.LocalizedDescription ?? "The folder could not be remembered.");
            }

            var token = bookmark.GetBase64EncodedString(NSDataBase64EncodingOptions.None);
            var name = url.LastPathComponent ?? url.Path ?? "Folder";
            return new BackupFolderPick(token, name);
        }
        finally
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
        }
    }

    public Task<bool> CanWriteAsync(string token) => Task.Run(() =>
    {
        var url = Resolve(token);
        if (url?.Path is null) return false;

        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            return NSFileManager.DefaultManager.IsWritableFile(url.Path);
        }
        finally
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
        }
    });

    public Task WriteFileAsync(string token, string fileName, string sourcePath) => Task.Run(() =>
    {
        var url = Resolve(token) ?? throw new IOException("The backup folder is no longer reachable.");

        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            var target = url.Append(fileName, false);
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
        finally
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
        }
    });

    public Task<IReadOnlyList<string>> ListFileNamesAsync(string token) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var url = Resolve(token);
        if (url?.Path is null) return Array.Empty<string>();

        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            var names = NSFileManager.DefaultManager.GetDirectoryContent(url.Path, out var error);
            return error is null && names is not null ? names.ToList() : Array.Empty<string>();
        }
        finally
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
        }
    });

    public Task DeleteFileAsync(string token, string fileName) => Task.Run(() =>
    {
        var url = Resolve(token);
        if (url is null) return;

        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            NSFileManager.DefaultManager.Remove(url.Append(fileName, false), out _);
        }
        finally
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
        }
    });

    private static NSUrl? Resolve(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        if (token == BackupFolderPick.DeviceToken)
        {
            var documents = NSFileManager.DefaultManager
                .GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User)
                .FirstOrDefault();
            if (documents?.Path is null) return null;

            var backups = Path.Combine(documents.Path, "Backups");
            Directory.CreateDirectory(backups);
            // Starting a security scope on a plain sandbox URL answers false, and the callers skip the
            // matching stop on false, so the device folder needs no special handling past this point.
            return NSUrl.FromFilename(backups);
        }

        var data = new NSData(token, NSDataBase64DecodingOptions.None);
        var url = NSUrl.FromBookmarkData(data, NSUrlBookmarkResolutionOptions.WithoutUI, null, out _, out var error);
        return error is null ? url : null;
    }

    private sealed class PickerDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<NSUrl?> _tcs;

        public PickerDelegate(TaskCompletionSource<NSUrl?> tcs) => _tcs = tcs;

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls) =>
            _tcs.TrySetResult(urls.FirstOrDefault());

        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            _tcs.TrySetResult(null);
    }
}
