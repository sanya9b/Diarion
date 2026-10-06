using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>
/// Abstraction over platform biometric authentication (Face ID / fingerprint), so lock logic
/// stays testable and free of platform dependencies.
/// </summary>
public interface IBiometricService
{
    Task<bool> IsAvailableAsync();
    Task<bool> AuthenticateAsync(string title, string reason);
}
