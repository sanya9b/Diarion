using System;
using System.Security.Cryptography;
using System.Text;

namespace Diarion.Helpers;

/// <summary>
/// Hashes and verifies the app-lock PIN using PBKDF2 (SHA-256) with a random per-PIN salt.
/// Pure and side-effect free so it can be unit-tested without platform storage.
/// </summary>
public static class PinHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string Salt, string Hash) Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin ?? string.Empty), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string pin, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(pin ?? string.Empty), salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
