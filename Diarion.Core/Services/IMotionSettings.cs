namespace Diarion.Services;

/// <summary>
/// The platform's "reduce motion" accessibility preference, behind an interface so animation
/// decisions stay testable and the UI layer never asks the OS directly.
/// </summary>
public interface IMotionSettings
{
    /// <summary>True when the user asked the system to minimise animation.</summary>
    bool ReduceMotion { get; }
}
