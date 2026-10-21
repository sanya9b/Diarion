using Android.App;
using Android.Content.PM;
using Android.OS;
using Plugin.Fingerprint;

namespace Diarion;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// The home-screen shortcut ("Quick entry") arrives as an intent with this action; MAUI's own
// OnNewIntent/OnResume hand it to AppActions, which App.OnAppActionActivated listens to. A static
// filter, not a permission: the allowlist in the manifest is unchanged.
[IntentFilter(new[] { Microsoft.Maui.ApplicationModel.Platform.Intent.ActionAppAction }, Categories = new[] { Android.Content.Intent.CategoryDefault })]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Plugin.Fingerprint (v3/MAUI) needs the current activity to host the biometric prompt.
        CrossFingerprint.SetCurrentActivityResolver(() => this);

        // The folder picker for scheduled backups. Registered here, before the activity starts, as
        // the activity-result API requires; the service launches it and gets the tree URI back
        // through the callback. No storage permission is involved — the grant is per folder.
        Diarion.Services.AndroidBackupFolderService.Launcher = RegisterForActivityResult(
            new AndroidX.Activity.Result.Contract.ActivityResultContracts.OpenDocumentTree(),
            new FolderPickedCallback());
    }

    private sealed class FolderPickedCallback : Java.Lang.Object, AndroidX.Activity.Result.IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result) =>
            Diarion.Services.AndroidBackupFolderService.OnFolderPicked(result as Android.Net.Uri);
    }
}
