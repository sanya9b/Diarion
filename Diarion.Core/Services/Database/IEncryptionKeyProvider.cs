namespace Diarion.Services.Database;

/// <summary>
/// Provides the password used to encrypt the local LiteDB database at rest.
/// Implementations MUST store the key in platform-backed secure storage
/// (iOS Keychain / Android Keystore) and MUST never persist it in the database,
/// preferences, backups, or logs.
/// </summary>
public interface IEncryptionKeyProvider
{
    /// <summary>
    /// Returns the database encryption key, creating and securely persisting a new random
    /// key on first use. May block briefly on platform secure storage, so it is intended to
    /// be called once during database initialization.
    /// </summary>
    string GetOrCreateKey();
}
