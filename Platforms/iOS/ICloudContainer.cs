using System.Threading.Tasks;
using Foundation;

namespace Diarion.Services;

/// <summary>
/// The app's iCloud container (spec 21), resolved once. <c>GetUrlForUbiquityContainer</c> can block
/// while the system sets the container up, so it runs on the thread pool and everyone shares the
/// answer. Null means no entitlement in this build, or no iCloud account on the phone.
/// </summary>
internal static class ICloudContainer
{
    public const string Identifier = "iCloud.org.diarion.app";

    private static readonly object Gate = new();
    private static Task<NSUrl?>? _root;

    /// <summary>The container root, resolved on first use.</summary>
    public static Task<NSUrl?> RootAsync()
    {
        lock (Gate)
        {
            return _root ??= Task.Run(() =>
            {
                try
                {
                    if (NSFileManager.DefaultManager.UbiquityIdentityToken is null) return null;
                    return NSFileManager.DefaultManager.GetUrlForUbiquityContainer(Identifier);
                }
                catch
                {
                    return null;
                }
            });
        }
    }

    /// <summary>The root if it has already been resolved; null while the first resolution is still running.</summary>
    public static NSUrl? RootIfKnown
    {
        get
        {
            var task = RootAsync();
            return task.IsCompletedSuccessfully ? task.Result : null;
        }
    }

    /// <summary>Signed in to iCloud at all, without touching the container.</summary>
    public static bool IsSignedIn => NSFileManager.DefaultManager.UbiquityIdentityToken is not null;
}
