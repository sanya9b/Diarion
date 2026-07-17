using System;
using System.Threading.Tasks;
using Diarion.Helpers;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Stores the app-lock PIN (salted PBKDF2 hash) and biometric flag in platform SecureStorage,
/// never in the database. Provides a synchronous facade over the async SecureStorage API (same
/// pattern as <see cref="SecureStorageKeyProvider"/>).
/// Brute-force protection: after <see cref="MaxAttempts"/> wrong PINs, verification is locked out
/// for <see cref="LockoutSeconds"/> seconds (tracked in memory).
/// </summary>
public class AppLockService : IAppLockService
{
    private const string PinSaltKey = "diarion_pin_salt";
    private const string PinHashKey = "diarion_pin_hash";
    private const string BiometricKey = "diarion_biometric_enabled";
    private const int MaxAttempts = 5;
    private const int LockoutSeconds = 30;

    private int _failedAttempts;
    private DateTime? _lockoutUntilUtc;

    public bool IsLockEnabled => !string.IsNullOrEmpty(GetSecure(PinHashKey));

    public bool IsBiometricEnabled
    {
        get => GetSecure(BiometricKey) == "1";
        set => SetSecure(BiometricKey, value ? "1" : "0");
    }

    public TimeSpan? LockoutRemaining
    {
        get
        {
            if (_lockoutUntilUtc is { } until)
            {
                var remaining = until - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : null;
            }
            return null;
        }
    }

    public void SetPin(string pin)
    {
        var (salt, hash) = PinHasher.Hash(pin);
        SetSecure(PinSaltKey, salt);
        SetSecure(PinHashKey, hash);
        _failedAttempts = 0;
        _lockoutUntilUtc = null;
    }

    public void RemovePin()
    {
        RemoveSecure(PinSaltKey);
        RemoveSecure(PinHashKey);
        RemoveSecure(BiometricKey);
        _failedAttempts = 0;
        _lockoutUntilUtc = null;
    }

    public PinVerifyResult VerifyPin(string pin)
    {
        if (LockoutRemaining is { } rem && rem > TimeSpan.Zero)
        {
            return PinVerifyResult.LockedOut;
        }

        var salt = GetSecure(PinSaltKey) ?? string.Empty;
        var hash = GetSecure(PinHashKey) ?? string.Empty;

        if (PinHasher.Verify(pin, salt, hash))
        {
            _failedAttempts = 0;
            _lockoutUntilUtc = null;
            return PinVerifyResult.Success;
        }

        _failedAttempts++;
        if (_failedAttempts >= MaxAttempts)
        {
            _lockoutUntilUtc = DateTime.UtcNow.AddSeconds(LockoutSeconds);
            _failedAttempts = 0;
            return PinVerifyResult.LockedOut;
        }

        return PinVerifyResult.Wrong;
    }

    private static string? GetSecure(string key)
    {
        try
        {
            return Task.Run(() => SecureStorage.Default.GetAsync(key)).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static void SetSecure(string key, string value)
    {
        try
        {
            Task.Run(() => SecureStorage.Default.SetAsync(key, value)).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort; a failure here surfaces as lock-not-set rather than a crash.
        }
    }

    private static void RemoveSecure(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch
        {
            // Best-effort.
        }
    }
}
