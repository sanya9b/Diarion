using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Diarion.Services.Database;
using Foundation;
using Microsoft.Maui.Storage;
using Security;

namespace Diarion.Services;

/// <summary>
/// The database key as a Keychain item that iCloud Keychain carries to the user's other iPhones
/// (spec 21, D5), so a recovery copy made on one phone opens on the next. Replaces
/// <see cref="SecureStorageKeyProvider"/> on iOS; the value it kept is adopted, never replaced.
/// <para>
/// Order matters and is deliberate: a key already on this phone always wins. A phone with its own
/// diary must not pick up a synced key from another phone and find its database unreadable. Only a
/// phone with no key of its own takes the synced one — a fresh install, which is exactly the case
/// the feature exists for. Reads fail closed, as before: a Keychain that cannot answer is not a
/// reason to generate a key that would lock the diary.
/// </para>
/// </summary>
public sealed class IosKeychainKeyProvider : IEncryptionKeyProvider
{
    private const string LegacyKeyName = "diarion_db_encryption_key";
    private const string Service = "org.diarion.app.database";
    private const string Account = "diarion_db_encryption_key";

    private readonly object _gate = new();
    private string? _cached;

    public string GetOrCreateKey()
    {
        lock (_gate)
        {
            if (_cached is not null) return _cached;

            // 1. The key this phone has used so far.
            var local = Task.Run(() => SecureStorage.Default.GetAsync(LegacyKeyName)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(local))
            {
                EnsureSynced(local);
                return _cached = local;
            }

            // 2. A key another phone of the same Apple ID published.
            var synced = ReadSynced();
            if (!string.IsNullOrEmpty(synced))
            {
                Task.Run(() => SecureStorage.Default.SetAsync(LegacyKeyName, synced)).GetAwaiter().GetResult();
                return _cached = synced;
            }

            // 3. Nothing anywhere: a new key, stored both ways.
            var fresh = GenerateKey();
            Task.Run(() => SecureStorage.Default.SetAsync(LegacyKeyName, fresh)).GetAwaiter().GetResult();
            WriteSynced(fresh);
            return _cached = fresh;
        }
    }

    /// <summary>Publishes the local key if nothing is published yet. An existing record is left alone.</summary>
    private static void EnsureSynced(string key)
    {
        try
        {
            if (ReadSynced() is null) WriteSynced(key);
        }
        catch (Exception ex)
        {
            // The local key works regardless; syncing is a convenience that can wait for the next launch.
            System.Diagnostics.Debug.WriteLine($"Keychain sync of the database key failed: {ex.Message}");
        }
    }

    private static SecRecord Query() => new(SecKind.GenericPassword)
    {
        Service = Service,
        Account = Account,
        Synchronizable = true
    };

    private static string? ReadSynced()
    {
        var record = SecKeyChain.QueryAsRecord(Query(), out var status);
        if (status == SecStatusCode.ItemNotFound) return null;
        if (status != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Keychain read failed: {status}");
        }

        return record?.ValueData?.ToString(NSStringEncoding.UTF8);
    }

    private static void WriteSynced(string key)
    {
        var record = Query();
        record.Accessible = SecAccessible.AfterFirstUnlock;
        record.ValueData = NSData.FromString(key, NSStringEncoding.UTF8);

        var status = SecKeyChain.Add(record);
        if (status == SecStatusCode.DuplicateItem)
        {
            status = SecKeyChain.Update(Query(), new SecRecord(SecKind.GenericPassword) { ValueData = record.ValueData });
        }

        if (status != SecStatusCode.Success)
        {
            throw new InvalidOperationException($"Keychain write failed: {status}");
        }
    }

    private static string GenerateKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
