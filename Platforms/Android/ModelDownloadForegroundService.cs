using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Diarion.Resources.Localization;
using Diarion.Services.Ai;

namespace Diarion;

/// <summary>
/// Exists so that a model download survives a locked screen.
/// </summary>
/// <remarks>
/// It fetches nothing. The HTTP loop stays in <c>ModelDownloadService</c>, where the resume logic,
/// the Wi-Fi rule and the SHA-256 check are already written and tested; duplicating any of that
/// into a platform service — or handing it to the system <c>DownloadManager</c>, which knows none
/// of it — would mean two implementations of the same rules for one feature.
///
/// What this provides is priority. Android throttles the CPU and the network of a process that
/// left the foreground, so the download the user started simply stops making progress once the
/// screen goes dark. A running foreground service says the process is doing something the user
/// asked for, and the system leaves it alone.
///
/// The notification is not decoration: from Android 8 it is the price of the privilege, and it is
/// also the only control the user has over a download while the app is not on screen — hence the
/// cancel action, which comes back in through <see cref="OnStartCommand"/>.
/// </remarks>
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class ModelDownloadForegroundService : Service
{
    public const string ActionStart = "com.diarion.app.action.MODEL_DOWNLOAD_START";
    public const string ActionCancel = "com.diarion.app.action.MODEL_DOWNLOAD_CANCEL";

    public const string ExtraModelId = "com.diarion.app.extra.MODEL_ID";
    public const string ExtraModelName = "com.diarion.app.extra.MODEL_NAME";

    /// <summary>One download at a time, so one notification, so one fixed id to update in place.</summary>
    public const int NotificationId = 4610;

    private const string ChannelId = "diarion.model-downloads";

    /// <summary>Passed to <see cref="NotificationCompat.Builder.SetProgress"/> for an indeterminate bar.</summary>
    public const int Indeterminate = -1;

    /// <summary>Permille rather than percent: on a 1.1 GB model a percent is eleven megabytes of stillness.</summary>
    public const int ProgressScale = 1000;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var modelId = intent?.GetStringExtra(ExtraModelId) ?? string.Empty;
        var modelName = intent?.GetStringExtra(ExtraModelName) ?? string.Empty;

        // A typed foreground service arrived in API 29, and this app supports 26. Below 29 there is
        // no type to declare — zero is what ServiceCompat expects, and the service runs untyped,
        // which is exactly how every foreground service worked on those versions.
        var type = OperatingSystem.IsAndroidVersionAtLeast(29)
            ? (int)global::Android.Content.PM.ForegroundService.TypeDataSync
            : 0;

        // First, and within five seconds of the start request — including the cancel intent, which
        // arrives through this same door and would otherwise leave the promise unkept.
        ServiceCompat.StartForeground(
            this,
            NotificationId,
            Build(this, modelId, modelName, string.Empty, Indeterminate),
            type);

        if (intent?.Action == ActionCancel)
        {
            Cancel(modelId);
        }

        // Restarting this without the download it was created for would show a notification for
        // nothing. The partial file survives regardless: GetState reads it as Interrupted and one
        // tap resumes from where it stopped.
        return StartCommandResult.NotSticky;
    }

    /// <summary>
    /// The notification the whole feature is visible through. Built here rather than in the host so
    /// the first one and every update after it are the same notification, not two that differ.
    /// </summary>
    public static Notification Build(Context context, string modelId, string modelName, string detail, int permille)
    {
        EnsureChannel(context);

        var text = string.IsNullOrEmpty(detail)
            ? modelName
            : string.IsNullOrEmpty(modelName) ? detail : $"{modelName} · {detail}";

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(AppResources.AiDownloadNotificationTitle)!
            .SetContentText(text)!
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)!
            .SetCategory(NotificationCompat.CategoryProgress)!
            .SetPriority(NotificationCompat.PriorityLow)!
            // Ongoing so it cannot be swiped away while the download it controls is still running.
            .SetOngoing(true)!
            // Without this every progress update re-alerts, and a gigabyte is a great many buzzes.
            .SetOnlyAlertOnce(true)!
            .SetProgress(ProgressScale, Math.Max(permille, 0), permille < 0)!;

        var open = OpenAppIntent(context);
        if (open is not null)
        {
            builder.SetContentIntent(open);
        }

        var cancel = CancelIntent(context, modelId);
        if (cancel is not null)
        {
            // The only control the user has over the download while the app is not on screen.
            builder.AddAction(0, AppResources.AiCancelAction, cancel);
        }

        // Build() is bound as nullable-returning; it has no path that returns one.
        return builder.Build()!;
    }

    private static PendingIntent? OpenAppIntent(Context context)
    {
        var launch = context.PackageName is { } package
            ? context.PackageManager?.GetLaunchIntentForPackage(package)
            : null;

        return launch is null
            ? null
            : PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable);
    }

    private static PendingIntent? CancelIntent(Context context, string modelId)
    {
        var intent = new Intent(context, typeof(ModelDownloadForegroundService))
            .SetAction(ActionCancel)!
            .PutExtra(ExtraModelId, modelId);

        // GetForegroundService, not GetService: by the time this is tapped the app is usually in
        // the background, where starting an ordinary service is refused outright.
        return PendingIntent.GetForegroundService(context, 0, intent, PendingIntentFlags.Immutable);
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        if (context.GetSystemService(NotificationService) is not NotificationManager manager)
        {
            return;
        }

        // Low importance: this is a progress bar, not news. Creating an existing channel is a
        // no-op, so this stays next to the notification it belongs to rather than in app startup,
        // where it would be created on every launch for a feature most users never touch.
        var channel = new NotificationChannel(
            ChannelId,
            AppResources.AiDownloadNotificationChannel,
            NotificationImportance.Low)
        {
            Description = AppResources.AiDownloadNotificationChannelDescription,
        };

        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Stops the download the notification is about. The service does not stop itself here: the
    /// download's own cleanup releases the host, and that is the single path off this screen.
    /// </summary>
    private void Cancel(string modelId)
    {
        if (string.IsNullOrEmpty(modelId))
        {
            return;
        }

        var downloads = IPlatformApplication.Current?.Services.GetService<IModelDownloadService>();
        if (downloads is null)
        {
            // The process was rebuilt without the app behind it. Nothing is downloading, so the
            // notification is a ghost — take it down rather than leave a dead cancel button.
            StopSelf();
            return;
        }

        downloads.Cancel(modelId);
    }
}
