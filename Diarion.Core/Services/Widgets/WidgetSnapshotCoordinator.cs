using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Widgets;

namespace Diarion.Services.Widgets;

/// <summary>
/// Keeps the widget snapshot in step with the diary (spec 20, R4). Listens for the saves that change
/// what a widget shows — a diary entry (mood, habits), a task write in the store, the widget settings,
/// the app lock —
/// and republishes a couple of seconds after the last one, so a burst of habit ticks costs one write.
/// <para>
/// Switching the app lock on is the one event handled at once and without the debounce: the user's
/// choice of level is dropped back to the default, the file is removed, and a button-only snapshot is
/// written in its place — so no number from before the lock survives it (AC2).
/// </para>
/// <para>
/// Nothing here may throw into the app: a widget that is a few minutes stale is a nuisance, a crash
/// on saving a habit is not. Failures are written to the debug log and the next change tries again.
/// </para>
/// </summary>
public sealed class WidgetSnapshotCoordinator
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);

    private readonly IWidgetSnapshotService _snapshots;
    private readonly IWidgetSnapshotPublisher _publisher;
    private readonly IProfileService _profiles;
    private readonly IAppLockService _lock;
    private readonly TimeProvider _clock;
    private readonly AsyncDebouncer _debouncer;
    private bool _started;

    public WidgetSnapshotCoordinator(
        IWidgetSnapshotService snapshots,
        IWidgetSnapshotPublisher publisher,
        IProfileService profiles,
        IAppLockService lockService,
        TimeProvider? clock = null,
        TimeSpan? debounce = null)
    {
        _snapshots = snapshots;
        _publisher = publisher;
        _profiles = profiles;
        _lock = lockService;
        _clock = clock ?? TimeProvider.System;
        _debouncer = new AsyncDebouncer(debounce ?? DefaultDebounce);
    }

    /// <summary>Subscribes and publishes once. A no-op on platforms without widgets, and on a second call.</summary>
    public void Start()
    {
        if (_started || !_publisher.IsSupported) return;
        _started = true;

        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<WidgetSnapshotCoordinator, DocumentChangedMessage>(this, static (self, m) =>
        {
            // Notes never reach a widget; only the diary carries mood and habits.
            if (m.SourceKind == DocumentKind.Diary) self.RequestRefresh();
        });
        messenger.Register<WidgetSnapshotCoordinator, TodoStoreChangedMessage>(this, static (self, _) => self.RequestRefresh());
        messenger.Register<WidgetSnapshotCoordinator, WidgetRefreshRequestedMessage>(this, static (self, _) => self.RequestRefresh());
        messenger.Register<WidgetSnapshotCoordinator, AppLockChangedMessage>(this, static (self, m) =>
        {
            if (m.IsEnabled) _ = self.OnLockEnabledAsync();
            else self.RequestRefresh();
        });

        RequestRefresh();
    }

    /// <summary>Republishes after the debounce window. Safe to call from any thread, any number of times.</summary>
    public void RequestRefresh()
    {
        if (!_publisher.IsSupported) return;
        _debouncer.Debounce(RefreshAsync);
    }

    /// <summary>Runs a pending refresh now. The app calls this on its way to the background.</summary>
    public Task FlushAsync() => _publisher.IsSupported ? _debouncer.FlushAsync() : Task.CompletedTask;

    /// <summary>Builds and publishes the snapshot at the level in force. Public for the tests and the flush.</summary>
    public async Task RefreshAsync()
    {
        if (!_publisher.IsSupported) return;
        try
        {
            var profile = await _profiles.GetUserProfileAsync();
            var level = WidgetPrivacyPolicy.Resolve(profile.WidgetPrivacyLevel, _lock.IsLockEnabled);
            var snapshot = await _snapshots.BuildAsync(_clock.GetLocalNow().Date, level);
            await _publisher.PublishAsync(WidgetSnapshotJson.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget snapshot not published: {ex}");
        }
    }

    /// <summary>The lock has just been switched on: forget the chosen level, remove the file, publish the button-only snapshot.</summary>
    public async Task OnLockEnabledAsync()
    {
        if (!_publisher.IsSupported) return;
        try
        {
            var profile = await _profiles.GetUserProfileAsync();
            if (profile.WidgetPrivacyLevel is not null)
            {
                profile.WidgetPrivacyLevel = null;
                await _profiles.SaveUserProfileAsync(profile);
            }

            await _publisher.ClearAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget snapshot not cleared on lock: {ex}");
        }
    }
}
