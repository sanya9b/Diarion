using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Provider;
using AndroidX.Activity.Result;
using Diarion.Services.Backup;
using AndroidUri = Android.Net.Uri;

namespace Diarion.Services;

/// <summary>
/// A document tree from the Storage Access Framework, with a persisted read/write grant. The user
/// picks the folder once in the system picker — Drive, a card, Downloads — and the grant survives
/// restarts; no storage permission is declared, and the manifest's allowlist is untouched.
/// </summary>
/// <remarks>
/// The picker is launched through the AndroidX activity-result API, registered by
/// <c>MainActivity</c> at creation, because that is the one form of "start an activity for a result"
/// that is not deprecated and does not need an override on the activity. Everything else is
/// <see cref="DocumentsContract"/> over a <see cref="ContentResolver"/>: there is no file path for a
/// tree URI, and pretending otherwise works on exactly one OEM.
/// </remarks>
public sealed class AndroidBackupFolderService : IBackupFolderService
{
    private const string Mime = "application/octet-stream";

    private static readonly object Gate = new();
    private static TaskCompletionSource<AndroidUri?>? _pending;

    /// <summary>Set once by <c>MainActivity.OnCreate</c>; null until the activity exists.</summary>
    public static ActivityResultLauncher? Launcher { get; set; }

    public bool IsSupported => true;

    public async Task<BackupFolderPick?> PickFolderAsync()
    {
        var launcher = Launcher ?? throw new InvalidOperationException("The folder picker is not registered yet.");

        TaskCompletionSource<AndroidUri?> tcs;
        lock (Gate)
        {
            _pending?.TrySetResult(null);
            tcs = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
        }

        // OpenDocumentTree takes an optional starting URI; none means the picker's own default.
        launcher.Launch(null);

        var uri = await tcs.Task;
        if (uri is null) return null;

        var resolver = Resolver;
        resolver.TakePersistableUriPermission(
            uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);

        return new BackupFolderPick(uri.ToString()!, DisplayNameOf(resolver, uri));
    }

    /// <summary>Called from the activity-result callback registered in <c>MainActivity</c>.</summary>
    public static void OnFolderPicked(AndroidUri? uri)
    {
        TaskCompletionSource<AndroidUri?>? tcs;
        lock (Gate)
        {
            tcs = _pending;
            _pending = null;
        }

        tcs?.TrySetResult(uri);
    }

    public Task<bool> CanWriteAsync(string token)
    {
        try
        {
            var tree = AndroidUri.Parse(token) ?? throw new ArgumentException("Not a URI.", nameof(token));
            var resolver = Resolver;

            var granted = resolver.PersistedUriPermissions
                .Any(p => p.Uri is not null && p.Uri.Equals(tree) && p.IsWritePermission);
            if (!granted) return Task.FromResult(false);

            using var cursor = resolver.Query(
                TreeDocument(tree), new[] { DocumentsContract.Document.ColumnDocumentId }, null, null, null);
            return Task.FromResult(cursor is not null && cursor.MoveToFirst());
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task WriteFileAsync(string token, string fileName, string sourcePath)
    {
        var tree = AndroidUri.Parse(token) ?? throw new ArgumentException("Not a URI.", nameof(token));
        var resolver = Resolver;

        // CreateDocument renames on collision ("name (1)") rather than replacing, so an existing
        // copy of the same name goes first — otherwise pruning would count it twice.
        var existing = FindChild(resolver, tree, fileName);
        if (existing is not null)
        {
            DocumentsContract.DeleteDocument(resolver, existing);
        }

        var created = DocumentsContract.CreateDocument(resolver, TreeDocument(tree), Mime, fileName)
            ?? throw new IOException("The folder did not accept a new file.");

        using var output = resolver.OpenOutputStream(created, "w")
            ?? throw new IOException("The new file could not be opened for writing.");
        using var input = File.OpenRead(sourcePath);
        await input.CopyToAsync(output);
        await output.FlushAsync();
    }

    public Task<IReadOnlyList<string>> ListFileNamesAsync(string token)
    {
        var tree = AndroidUri.Parse(token) ?? throw new ArgumentException("Not a URI.", nameof(token));
        var names = Children(Resolver, tree).Select(c => c.Name).ToList();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task DeleteFileAsync(string token, string fileName)
    {
        var tree = AndroidUri.Parse(token) ?? throw new ArgumentException("Not a URI.", nameof(token));
        var resolver = Resolver;

        var child = FindChild(resolver, tree, fileName);
        if (child is not null)
        {
            DocumentsContract.DeleteDocument(resolver, child);
        }

        return Task.CompletedTask;
    }

    private static ContentResolver Resolver =>
        Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("No content resolver.");

    /// <summary>The tree itself as a document URI — what CreateDocument wants as the parent.</summary>
    private static AndroidUri TreeDocument(AndroidUri tree) =>
        DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))
        ?? throw new InvalidOperationException("Could not address the folder.");

    private static AndroidUri? FindChild(ContentResolver resolver, AndroidUri tree, string name)
    {
        var child = Children(resolver, tree).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        return child.DocumentId is null
            ? null
            : DocumentsContract.BuildDocumentUriUsingTree(tree, child.DocumentId);
    }

    private static IEnumerable<(string? DocumentId, string Name)> Children(ContentResolver resolver, AndroidUri tree)
    {
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree));
        if (childrenUri is null) yield break;

        using var cursor = resolver.Query(
            childrenUri,
            new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName },
            null, null, null);
        if (cursor is null) yield break;

        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0);
            var name = cursor.GetString(1);
            if (name is not null) yield return (id, name);
        }
    }

    private static string DisplayNameOf(ContentResolver resolver, AndroidUri tree)
    {
        try
        {
            using var cursor = resolver.Query(
                TreeDocument(tree), new[] { DocumentsContract.Document.ColumnDisplayName }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var name = cursor.GetString(0);
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        catch
        {
            // Fall through to the id, which is readable enough: "primary:Diarion".
        }

        var id = DocumentsContract.GetTreeDocumentId(tree) ?? tree.LastPathSegment ?? "Folder";
        return id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
    }
}
