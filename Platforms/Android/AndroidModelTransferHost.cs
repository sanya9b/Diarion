using Android.Content;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Diarion.ViewModels;
using Application = Android.App.Application;

namespace Diarion.Services.Ai;

/// <summary>
/// The Android answer to "can it download while minimized": yes, for as long as a foreground
/// service is up.
/// </summary>
/// <remarks>
/// See <see cref="ModelDownloadForegroundService"/> for why the service exists at all. This class
/// is the thin part — it starts it, keeps its notification current, and stops it — and it lives on
/// the platform side of <see cref="IModelTransferHost"/> so none of that reaches Core.
/// </remarks>
public sealed class AndroidModelTransferHost : IModelTransferHost
{
    /// <summary>
    /// Android quietly rate-limits a notification updated more often than this — the updates stop
    /// arriving rather than being queued, so the bar would freeze at whatever got through last.
    /// </summary>
    private const long UpdateIntervalMs = 1000;

    private readonly object _gate = new();

    private string _modelId = string.Empty;
    private string _modelName = string.Empty;
    private long _lastUpdateMs;
    private bool _running;

    public bool KeepsRunningInBackground => true;

    public IDisposable Begin(string modelId, string modelName)
    {
        lock (_gate)
        {
            _modelId = modelId;
            _modelName = modelName;
            _lastUpdateMs = 0;
            _running = true;
        }

        var context = Application.Context;
        var intent = new Intent(context, typeof(ModelDownloadForegroundService))
            .SetAction(ModelDownloadForegroundService.ActionStart)!
            .PutExtra(ModelDownloadForegroundService.ExtraModelId, modelId)
            .PutExtra(ModelDownloadForegroundService.ExtraModelName, modelName);

        ContextCompat.StartForegroundService(context, intent);

        return new Handle(this);
    }

    public void Report(ModelDownloadProgress progress)
    {
        string modelId;
        string modelName;

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            var now = Java.Lang.JavaSystem.CurrentTimeMillis();

            // Verification is exempt: it arrives once, and it is the update that explains why the
            // bar is about to sit still for ten seconds.
            if (progress.Phase == ModelDownloadPhase.Transferring && now - _lastUpdateMs < UpdateIntervalMs)
            {
                return;
            }

            _lastUpdateMs = now;
            modelId = _modelId;
            modelName = _modelName;
        }

        var permille = progress.Phase == ModelDownloadPhase.Verifying
            ? ModelDownloadForegroundService.Indeterminate
            : (int)Math.Round(progress.Fraction * ModelDownloadForegroundService.ProgressScale);

        var notification = ModelDownloadForegroundService.Build(
            Application.Context,
            modelId,
            modelName,
            ModelProgressText.Describe(progress),
            permille);

        // Same id as the one the service went foreground with, so this replaces it in place rather
        // than stacking. If the user denied POST_NOTIFICATIONS this does nothing and the download
        // still runs — invisible, but not stopped.
        NotificationManagerCompat.From(Application.Context)?
            .Notify(ModelDownloadForegroundService.NotificationId, notification);
    }

    private void Release()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
        }

        var context = Application.Context;
        context.StopService(new Intent(context, typeof(ModelDownloadForegroundService)));
    }

    /// <summary>
    /// One download's claim on the foreground. Idempotent, because Core disposes it in a
    /// <c>finally</c> that also runs for endings nobody planned.
    /// </summary>
    private sealed class Handle(AndroidModelTransferHost owner) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            owner.Release();
        }
    }
}
