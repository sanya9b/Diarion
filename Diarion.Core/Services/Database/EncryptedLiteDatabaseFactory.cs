using System;
using System.IO;
using System.Linq;
using LiteDB;

namespace Diarion.Services.Database;

/// <summary>
/// Opens the local LiteDB database with AES encryption, transparently migrating a pre-existing
/// UNENCRYPTED database file to an encrypted one on first run.
/// <para>
/// The migration copies every document into a fresh encrypted file and then atomically swaps it
/// in (keeping a backup), so a crash, a wrong key, or a lost key can never corrupt or destroy the
/// original data. Detection is deterministic (it probes whether the existing file opens without a
/// password) rather than relying on edge-case LiteDB error behaviour.
/// </para>
/// </summary>
public static class EncryptedLiteDatabaseFactory
{
    private const string BackupSuffix = ".premigration.bak";
    private const string MigratingSuffix = ".migrating.tmp";

    /// <summary>
    /// Opens (and, if needed, migrates) the encrypted database at <paramref name="path"/>.
    /// When <paramref name="password"/> is null/empty the database is opened WITHOUT encryption
    /// (used only when no key provider is configured, e.g. certain tests).
    /// </summary>
    public static LiteDatabase Open(string path, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new LiteDatabase(new ConnectionString { Filename = path });
        }

        var encrypted = new ConnectionString { Filename = path, Password = password };

        // Fresh install: creating a new file with a password yields an encrypted database.
        if (!File.Exists(path))
        {
            return new LiteDatabase(encrypted);
        }

        // Legacy plaintext database from before encryption was introduced -> migrate.
        if (CanOpenUnencrypted(path))
        {
            return MigrateToEncrypted(path, password, encrypted);
        }

        // Already encrypted: open with the key. Throws (fail-closed) if the key is wrong/lost,
        // rather than touching the data file.
        return new LiteDatabase(encrypted);
    }

    /// <summary>
    /// Returns true only if <paramref name="path"/> is a LiteDB database that opens with
    /// <paramref name="password"/> and contains <paramref name="requiredCollection"/>. Used to
    /// validate a restore candidate before overwriting live data — this rejects foreign, corrupt,
    /// or wrong-key/wrong-device files.
    /// </summary>
    public static bool IsValidEncryptedDatabase(string path, string? password, string requiredCollection, int maxUserVersion = int.MaxValue)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var cs = string.IsNullOrEmpty(password)
                ? new ConnectionString { Filename = path }
                : new ConnectionString { Filename = path, Password = password };

            using var db = new LiteDatabase(cs);
            // Reject a backup created by a newer app schema (would otherwise be silently downgraded).
            return db.GetCollectionNames().Contains(requiredCollection) && db.UserVersion <= maxUserVersion;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanOpenUnencrypted(string path)
    {
        try
        {
            using var plain = new LiteDatabase(new ConnectionString { Filename = path });
            _ = plain.GetCollectionNames(); // force the header/first page to be read
            return true;
        }
        catch
        {
            return false; // encrypted (or unreadable) -> not a plaintext file we should migrate
        }
    }

    private static LiteDatabase MigrateToEncrypted(string path, string password, ConnectionString encrypted)
    {
        var tempPath = path + MigratingSuffix;
        var backupPath = path + BackupSuffix;
        SafeDelete(tempPath);
        SafeDelete(backupPath);

        try
        {
            // Copy every document from the plaintext DB into a fresh encrypted DB.
            using (var plain = new LiteDatabase(new ConnectionString { Filename = path }))
            using (var enc = new LiteDatabase(new ConnectionString { Filename = tempPath, Password = password }))
            {
                CopyAllCollections(plain, enc);
            }

            // Atomically replace the plaintext file with the encrypted one, keeping the original
            // as a backup until we have verified the new file opens with the key.
            File.Replace(tempPath, path, backupPath);

            var db = new LiteDatabase(encrypted); // verify
            SafeDelete(backupPath);
            return db;
        }
        catch (Exception)
        {
            SafeDelete(tempPath);

            // If the swap already happened (backup exists) but a later step failed, restore the
            // original plaintext file so no data is lost.
            if (File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, path, overwrite: true);
                    SafeDelete(backupPath);
                }
                catch
                {
                    // Leave the .bak in place for manual recovery.
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Writes a copy of the database at <paramref name="sourcePath"/> to <paramref name="destinationPath"/>
    /// under a different key. Used to produce a portable backup, whose key comes from the user's
    /// passphrase rather than from this device's keystore — a plain file copy would be readable only
    /// on the device that made it, which is the whole defect this exists to fix.
    /// <para>The caller is responsible for closing the live database first.</para>
    /// </summary>
    public static void ReencryptTo(string sourcePath, string? sourcePassword, string destinationPath, string destinationPassword)
    {
        if (string.IsNullOrEmpty(destinationPassword))
        {
            throw new ArgumentException("A portable backup must be encrypted.", nameof(destinationPassword));
        }

        var source = string.IsNullOrEmpty(sourcePassword)
            ? new ConnectionString { Filename = sourcePath }
            : new ConnectionString { Filename = sourcePath, Password = sourcePassword };

        using var from = new LiteDatabase(source);
        using var to = new LiteDatabase(new ConnectionString { Filename = destinationPath, Password = destinationPassword });

        CopyAllCollections(from, to);
    }

    /// <summary>
    /// Copies documents and the schema version. <c>UserVersion</c> has to travel with the data: the
    /// migration runner reads it to decide what still needs applying, and a copy that lost it would be
    /// re-migrated from zero on restore.
    /// </summary>
    private static void CopyAllCollections(LiteDatabase source, LiteDatabase destination)
    {
        foreach (var name in source.GetCollectionNames())
        {
            var docs = source.GetCollection<BsonDocument>(name).FindAll().ToList();
            if (docs.Count > 0)
            {
                destination.GetCollection<BsonDocument>(name).InsertBulk(docs);
            }
        }

        destination.UserVersion = source.UserVersion;
        destination.Checkpoint();
    }

    private static void SafeDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best-effort cleanup; a leftover temp/.bak file is harmless.
        }
    }
}
