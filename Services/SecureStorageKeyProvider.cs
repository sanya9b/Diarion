using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Diarion.Services.Database;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Supplies the LiteDB encryption key from platform secure storage (iOS Keychain / Android
/// Keystore-backed), generating a 256-bit random key on first use. The key is never written to
/// the database, preferences, backups, or logs.
/// </summary>
public class SecureStorageKeyProvider : IEncryptionKeyProvider
{
    private const string KeyName = "diarion_db_encryption_key";

    public string GetOrCreateKey()
    {
        // SecureStorage exposes only an async API; run it on the thread pool (no captured context)
        // so this can be called synchronously during database initialization without deadlocking.
        return Task.Run(GetOrCreateKeyAsync).GetAwaiter().GetResult();
    }

    private static async Task<string> GetOrCreateKeyAsync()
    {
        // Intentionally NOT swallowing read errors: silently regenerating the key would make an
        // existing encrypted database unreadable. Fail closed instead.
        var existing = await SecureStorage.Default.GetAsync(KeyName);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var key = GenerateKey();
        await SecureStorage.Default.SetAsync(KeyName, key);
        return key;
    }

    private static string GenerateKey()
    {
        var bytes = new byte[32]; // 256-bit
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
