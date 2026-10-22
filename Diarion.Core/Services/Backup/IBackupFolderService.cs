using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diarion.Services.Backup;

/// <summary>A folder the user picked once, remembered by an opaque token the platform can reopen later.</summary>
/// <param name="Token">Android: the tree URI with a persisted grant. iOS: a security-scoped bookmark, base64. Windows: a FutureAccessList token.</param>
/// <param name="DisplayName">What to call the folder on the settings screen.</param>
public sealed record BackupFolderPick(string Token, string DisplayName)
{
    /// <summary>
    /// The token of the folder on the phone itself, which needs no picking and no grant: Files → On My
    /// iPhone → Diarion on iOS, Download/Diarion on Android. The default place for copies since
    /// 2026-10-23 (owner: "хочу щоб в телефон бекап автоматично зберігався").
    /// </summary>
    public const string DeviceToken = "device";

    public bool IsDevice => Token == DeviceToken;
}

/// <summary>
/// A folder outside the app's sandbox that the app may write to again without asking: a place in
/// iCloud Drive, Google Drive, a memory card, OneDrive. The whole point of a scheduled backup is
/// that it lands somewhere the loss of the phone does not take with it.
/// </summary>
/// <remarks>
/// Every method takes the token rather than a path. On Android the folder is a document tree, not a
/// path at all; on iOS the path is only valid between a start and a stop of security-scoped access.
/// Handing out a path would work on the one platform where it is not needed and nowhere else.
/// Nothing here opens a network connection: the system's own file provider does the uploading, and
/// the app never learns whether it did.
/// </remarks>
public interface IBackupFolderService
{
    /// <summary>False where no folder provider exists; the settings card says so instead of offering a switch.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// The folder on the phone, with <see cref="BackupFolderPick.DeviceToken"/> as its token and a name
    /// the settings card can show; null where the platform has no such place. Every method below accepts
    /// its token like any picked one.
    /// </summary>
    BackupFolderPick? DeviceFolder { get; }

    /// <summary>Opens the platform's folder picker. Null when the user backed out.</summary>
    Task<BackupFolderPick?> PickFolderAsync();

    /// <summary>Whether the grant behind the token still works — it may have been revoked, or the folder deleted.</summary>
    Task<bool> CanWriteAsync(string token);

    /// <summary>Copies a finished local file into the folder under <paramref name="fileName"/>, replacing any file of that name.</summary>
    Task WriteFileAsync(string token, string fileName, string sourcePath);

    /// <summary>Names of the files directly in the folder. Used to prune old backups; nothing else is ever read.</summary>
    Task<IReadOnlyList<string>> ListFileNamesAsync(string token);

    Task DeleteFileAsync(string token, string fileName);
}
