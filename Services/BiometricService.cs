using System.Threading.Tasks;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace Diarion.Services;

public class BiometricService : IBiometricService
{
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await CrossFingerprint.Current.IsAvailableAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AuthenticateAsync(string title, string reason)
    {
        try
        {
            // AllowAlternativeAuthentication = false: the app provides its own PIN fallback, so we
            // must not defer to the OS device credential (which would sit outside our lock).
            var config = new AuthenticationRequestConfiguration(title, reason)
            {
                AllowAlternativeAuthentication = false
            };

            var result = await CrossFingerprint.Current.AuthenticateAsync(config);
            return result.Authenticated;
        }
        catch
        {
            return false;
        }
    }
}
