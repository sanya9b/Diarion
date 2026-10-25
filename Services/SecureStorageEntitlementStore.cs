using System.Threading.Tasks;
using Diarion.Services.Store;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Access state in the platform keychain: on iOS it outlives a reinstall, which is what makes
/// "free months" for 1.0 users survive a new phone restored from backup. Same best-effort
/// sync-over-async shape as <see cref="AppLockService"/>, for the same reason — it is read before
/// the first frame.
/// </summary>
public sealed class SecureStorageEntitlementStore : IEntitlementStore
{
    private const string Prefix = "diarion.";

    public string? Get(string key)
    {
        try
        {
            return Task.Run(() => SecureStorage.Default.GetAsync(Prefix + key)).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    public void Set(string key, string value)
    {
        try
        {
            Task.Run(() => SecureStorage.Default.SetAsync(Prefix + key, value)).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort: a failure here means the state is re-derived next launch, not a crash.
        }
    }

    public void Remove(string key)
    {
        try
        {
            SecureStorage.Default.Remove(Prefix + key);
        }
        catch
        {
            // Best-effort.
        }
    }
}
