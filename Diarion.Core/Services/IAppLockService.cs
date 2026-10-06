using System;

namespace Diarion.Services;

public enum PinVerifyResult
{
    Success,
    Wrong,
    LockedOut
}

/// <summary>
/// Manages the app-lock credential. The lock is "enabled" iff a PIN is set. The PIN is stored
/// only as a salted hash in platform secure storage (never in the database). Biometric unlock is
/// an optional accelerator that is only meaningful while a PIN is set.
/// </summary>
public interface IAppLockService
{
    bool IsLockEnabled { get; }
    bool IsBiometricEnabled { get; set; }

    /// <summary>Remaining brute-force lockout, or null if not currently locked out.</summary>
    TimeSpan? LockoutRemaining { get; }

    void SetPin(string pin);
    void RemovePin();
    PinVerifyResult VerifyPin(string pin);
}
