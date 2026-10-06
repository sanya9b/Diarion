using System;
using System.Security.Cryptography;
using System.Text;

namespace Diarion.Helpers;

/// <summary>
/// Derives the encryption key for a portable backup from a user passphrase (PBKDF2-SHA256).
/// <para>
/// This is deliberately separate from <see cref="PinHasher"/> even though both use PBKDF2. The PIN
/// guards a UI barrier and is rate-limited by the app; a backup file is offline, so an attacker gets
/// unlimited attempts and the work factor has to be far higher.
/// </para>
/// <para>
/// The iteration count is written into every backup rather than assumed, so raising it later does not
/// make previously exported backups unreadable — which for this feature would be the exact failure it
/// exists to prevent.
/// </para>
/// </summary>
public static class BackupKeyDeriver
{
    /// <summary>Work factor for new backups. Read the value stored in the file when opening one.</summary>
    public const int CurrentIterations = 600_000;

    public const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>Upper bound accepted when reading a file, so a malformed header cannot hang the app.</summary>
    public const int MaxIterations = 10_000_000;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Derives the LiteDB password. Returned as base64 because LiteDB takes the key as a string and
    /// would otherwise re-encode whatever bytes we handed it.
    /// </summary>
    public static string DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        if (salt is not { Length: > 0 })
        {
            throw new ArgumentException("Salt must not be empty.", nameof(salt));
        }

        if (iterations is < 1 or > MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase ?? string.Empty),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return Convert.ToBase64String(key);
    }
}
