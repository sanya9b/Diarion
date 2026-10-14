namespace Diarion.Services;

/// <summary>
/// Reads the OS reduce-motion preference. Android has no dedicated switch: the accessibility
/// "Remove animations" toggle (and developer options) zero the animator duration scale instead.
/// </summary>
public sealed class MotionSettings : IMotionSettings
{
#if WINDOWS
    private static readonly Windows.UI.ViewManagement.UISettings WindowsSettings = new();
#endif

    public bool ReduceMotion
    {
        get
        {
            try
            {
#if ANDROID
                var resolver = Android.App.Application.Context.ContentResolver;
                var scale = Android.Provider.Settings.Global.GetFloat(
                    resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
                return scale == 0f;
#elif IOS || MACCATALYST
                return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif WINDOWS
                return !WindowsSettings.AnimationsEnabled;
#else
                return false;
#endif
            }
            catch
            {
                // A settings lookup failing must never cost the user an interaction; fall back to full motion.
                return false;
            }
        }
    }
}
