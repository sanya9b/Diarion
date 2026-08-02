using System;
using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>
/// Why a backup operation ended the way it did. This used to be a <c>bool</c>, which meant a failed
/// restore said nothing at all — and the most likely cause, a backup from another device, looked
/// identical to a corrupt file.
/// </summary>
public enum BackupOutcome
{
    Success,

    /// <summary>The user backed out of the file picker or the passphrase prompt. Not an error.</summary>
    Cancelled,

    /// <summary>The passphrase did not decrypt the backup.</summary>
    WrongPassphrase,

    /// <summary>Readable, but not a Diarion backup — or damaged beyond recognition.</summary>
    NotADiarionBackup,

    /// <summary>Made by a newer version of the app; restoring it would silently downgrade the schema.</summary>
    NewerSchema,

    /// <summary>
    /// A legacy device-key backup that this device cannot open. Distinct from a wrong passphrase
    /// because there is nothing the user can type to fix it — the key never left the old device.
    /// </summary>
    LegacyBackupFromAnotherDevice,

    Failed
}

public interface IBackupService
{
    /// <summary>
    /// Writes a portable backup encrypted with a key derived from the user's passphrase, then opens
    /// the share sheet. <paramref name="passphraseProvider"/> returns null if the user cancels.
    /// </summary>
    Task<BackupOutcome> ExportBackupAsync(Func<Task<string?>> passphraseProvider);

    /// <summary>
    /// Restores a backup over the live database. The passphrase is only requested once the picked
    /// file turns out to be a portable backup, so legacy same-device backups still restore without one.
    /// </summary>
    Task<BackupOutcome> ImportBackupAsync(Func<Task<string?>> passphraseProvider);
}
