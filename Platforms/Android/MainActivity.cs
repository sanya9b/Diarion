using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Diarion.Services.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Fingerprint;

namespace Diarion;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>The intent extra a home-screen widget puts its <c>diarion://</c> link in (spec 20, R5).</summary>
    public const string DeepLinkExtra = "org.diarion.app.deeplink";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleDeepLink(Intent);

        // Plugin.Fingerprint (v3/MAUI) needs the current activity to host the biometric prompt.
        CrossFingerprint.SetCurrentActivityResolver(() => this);

        // The folder picker for scheduled backups. Registered here, before the activity starts, as
        // the activity-result API requires; the service launches it and gets the tree URI back
        // through the callback. No storage permission is involved — the grant is per folder.
        Diarion.Services.AndroidBackupFolderService.Launcher = RegisterForActivityResult(
            new AndroidX.Activity.Result.Contract.ActivityResultContracts.OpenDocumentTree(),
            new FolderPickedCallback());
    }

    /// <summary>SingleTop: a tap while the app is alive comes here instead of a second OnCreate.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleDeepLink(intent);
    }

    private static void HandleDeepLink(Intent? intent)
    {
        var link = intent?.GetStringExtra(DeepLinkExtra);
        if (string.IsNullOrEmpty(link)) return;
        // Consumed once: a configuration change replays the same intent, and the user has moved on.
        intent!.RemoveExtra(DeepLinkExtra);
        IPlatformApplication.Current?.Services.GetService<IDeepLinkService>()?.Open(link);
    }

    private sealed class FolderPickedCallback : Java.Lang.Object, AndroidX.Activity.Result.IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result) =>
            Diarion.Services.AndroidBackupFolderService.OnFolderPicked(result as Android.Net.Uri);
    }
}
