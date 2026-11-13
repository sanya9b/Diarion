using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Diarion.Diagnostics;
using Diarion.Services;
using Diarion.Services.Store;
using Diarion.Services.Widgets;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Diarion;

public partial class App : Application
{
    internal const string OnboardingCompletedKey = "OnboardingCompleted";
    private static readonly TimeSpan RelockGrace = TimeSpan.FromSeconds(30);
    private DateTime? _backgroundedAtUtc;
    private bool _isLockShown;

    public App()
    {
        using var _ = StartupTrace.Measure("App..ctor");

        // Відновлюємо мову з налаштувань
        var savedLanguage = Preferences.Get("AppLanguage", string.Empty);
        if (!string.IsNullOrEmpty(savedLanguage))
        {
            var culture = new System.Globalization.CultureInfo(savedLanguage);
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            Diarion.Resources.Localization.AppResources.Culture = culture;
        }
        else
        {
            // Without this a fresh install never sets Culture, and anything resolved through
            // AppResources during database init would be frozen in the fallback language.
            Diarion.Resources.Localization.AppResources.Culture = System.Globalization.CultureInfo.CurrentUICulture;
        }

        InitializeComponent();

        // Відновлюємо тему з налаштувань за допомогою ThemeManager
        var currentTheme = Diarion.Services.ThemeManager.GetCurrentTheme();
        Diarion.Services.ThemeManager.SetTheme(currentTheme);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        using var _ = StartupTrace.Measure("App.CreateWindow");

        // Decide the root page BEFORE first paint. When the app lock is enabled we make LockPage
        // the window root (not a modal pushed after render) so the real content never flashes
        // before unlock. Services come from the app provider — the window Handler isn't attached
        // yet at CreateWindow time, so window.Handler?.MauiContext is null here.
        var services = IPlatformApplication.Current?.Services;
        var lockService = services?.GetService<IAppLockService>();
        // Resolved first on purpose: the install evidence for the legacy rule must be captured
        // before onboarding can complete in this launch, and the access state is restored from the
        // keychain before the first frame (spec 15, R1.2, R2.1).
        var entitlements = services?.GetService<IEntitlementService>();
        services?.GetService<PaywallPresenter>()?.Start();
        StartReminderCoordinator(services);
        // Widgets (spec 20): subscribes to the saves and publishes the first snapshot of this launch.
        services?.GetService<WidgetSnapshotCoordinator>()?.Start();
        // iCloud recovery (spec 21): follows the saves so the background copy knows there is something new.
        services?.GetService<Diarion.Services.Recovery.CloudRecoveryService>()?.Start();
        var deepLinks = services?.GetService<IDeepLinkService>();

        var window = new Window();

        if (services != null && lockService?.IsLockEnabled == true)
        {
            var lockPage = services.GetService<Diarion.Views.LockPage>();
            if (lockPage != null)
            {
                _isLockShown = true;
                lockPage.ViewModel.Unlocked = () =>
                {
                    _isLockShown = false;
                    _backgroundedAtUtc = null;
                    // Unlocked may fire off the UI thread (biometric continuation); swap on main.
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        window.Page = new AppShell();
                        deepLinks?.FlushPending();
                    });
                };
                window.Page = lockPage;
            }
        }

        // Fallback / no-lock path: AppShell is the root. If lock is enabled but LockPage couldn't
        // be resolved, the Created handler below still shows the lock modal (old behavior) rather
        // than leaking content — _isLockShown stays false so that path isn't suppressed.
        window.Page ??= new AppShell();

        window.Created += async (s, e) =>
        {
            RefreshEntitlements(entitlements);
            // Before onboarding: a fresh install whose iCloud holds a copy is offered it first (spec 21, D4).
            await OfferCloudRecoveryAsync(window);
            await CheckOnboardingAsync(window);
            await CheckSecurityAsync(window, coldStart: true);
            await AskCloudRecoveryConsentAsync(window);
            // A widget tap that started the app: by now the Shell is up and, without a lock, ready.
            deepLinks?.FlushPending();
            RetiredModelFiles.Sweep();
            ClearRetiredAppActions();
            StartScheduledBackup(services);
            StartCloudRecoveryCopy(services);
            ReportRolledBackMigration(services);
        };
        window.Resumed += async (s, e) =>
        {
            await CheckSecurityAsync(window, coldStart: false);
        };

        return window;
    }

    /// <summary>
    /// Build 16 published a "Quick entry" home-screen shortcut; the sheet it opened was removed on the
    /// owner's word (2026-10-23). The system keeps a published shortcut across app updates, so without
    /// this the icon would go on offering a menu item that opens nothing. Publishing an empty set clears
    /// it; on a phone that never had it, this is a no-op.
    /// </summary>
    private static void ClearRetiredAppActions()
    {
#if ANDROID || IOS
        _ = Task.Run(async () =>
        {
            try
            {
                if (AppActions.Current.IsSupported)
                {
                    await AppActions.Current.SetAsync(Array.Empty<AppAction>());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retired app actions not cleared: {ex.Message}");
            }
        });
#endif
    }

    /// <summary>
    /// A schema migration that threw was rolled back and the app opened on the previous schema
    /// (DatabaseContext.MigrationFailure). Nothing on screen says so; the crash report in Settings
    /// does, which is how the failure gets back to the developer instead of staying a mystery.
    /// </summary>
    private static void ReportRolledBackMigration(IServiceProvider? services)
    {
        if (services is null) return;
        _ = Task.Run(() =>
        {
            try
            {
                var failure = services.GetService<Diarion.Services.Database.IDatabaseContext>()?.MigrationFailure;
                if (failure is not null)
                    services.GetService<ICrashReporter>()?.Record("Schema migration (rolled back)", failure);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration status check failed: {ex}");
            }
        });
    }

    /// <summary>
    /// The scheduled backup's one trigger: a check when the app opens. Not a background job — iOS
    /// has twice been measured as giving none worth having — and not immediate: writing the copy
    /// closes and reopens the database for a moment, so it waits until well after the home screen
    /// has read its day. Does nothing unless the feature is on and the interval has passed.
    /// </summary>
    private static void StartScheduledBackup(IServiceProvider? services)
    {
        var scheduler = services?.GetService<Diarion.Services.Backup.IScheduledBackupService>();
        if (scheduler is null || !scheduler.IsSupported) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            try
            {
                await scheduler.RunIfDueAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scheduled backup check failed: {ex}");
            }
        });
    }

    /// <summary>
    /// Asks the store in the background. The service never downgrades the state for a missing
    /// answer, so nothing here is on the critical path of the first screen.
    /// </summary>
    /// <summary>Pauses or restores reminders to match the access state (spec 15, R1.6). Off the
    /// first frame's path: it reads the profile and habits from the database.</summary>
    private static void StartReminderCoordinator(IServiceProvider? services)
    {
        var coordinator = services?.GetService<ReminderAccessCoordinator>();
        if (coordinator is null) return;
        _ = Task.Run(coordinator.StartAsync);
    }

    private static void RefreshEntitlements(IEntitlementService? entitlements)
    {
        if (entitlements is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await entitlements.RefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Entitlement refresh failed: {ex}");
            }
        });
    }

    /// <summary>
    /// The paywall follows onboarding for a new user (spec 15, R4.2) — after the sheet is gone, so
    /// the two never stack. The presenter drops the request for anyone who may already write.
    /// </summary>
    private static async Task CloseOnboardingAsync(INavigation navigation)
    {
        await navigation.PopModalAsync();
        // The iCloud question comes right after onboarding and before the paywall: it is about the
        // diary the person has just decided to keep, and an alert and a sheet must not stack.
        await AskCloudRecoveryConsentAsync(IPlatformApplication.Current?.Services);
        WeakReferenceMessenger.Default.Send(new PaywallRequestedMessage(PaywallReason.AfterOnboarding));
        IPlatformApplication.Current?.Services.GetService<IDeepLinkService>()?.FlushPending();
    }

    private async Task CheckOnboardingAsync(Window window)
    {
        // DebugSandbox.ShowsOnboarding is a debug-only switch for looking at the first run again.
        if (!Diarion.Services.DebugSandbox.ShowsOnboarding && Preferences.Get(OnboardingCompletedKey, false)) return;

        var services = window.Handler?.MauiContext?.Services;
        var navigation = window.Page?.Navigation;
        if (services == null || navigation == null) return;

        var page = services.GetService<Diarion.Views.OnboardingPage>();
        if (page == null) return;

        page.ViewModel.Completed = () =>
        {
            if (!Diarion.Services.DebugSandbox.ShowsOnboarding)
                Preferences.Set(OnboardingCompletedKey, true);
            _ = CloseOnboardingAsync(navigation);
        };

        await navigation.PushModalAsync(page);
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _backgroundedAtUtc = DateTime.UtcNow;
        // The home screen is about to be looked at: a snapshot still waiting on its debounce goes out now.
        var coordinator = IPlatformApplication.Current?.Services.GetService<WidgetSnapshotCoordinator>();
        if (coordinator is not null) _ = coordinator.FlushAsync();
        // Likewise the iCloud copy, if something was written and an hour has passed (spec 21, D3).
        var recovery = IPlatformApplication.Current?.Services.GetService<Diarion.Services.Recovery.ICloudRecoveryService>();
        if (recovery is not null) _ = Task.Run(() => recovery.FlushOnBackgroundAsync());
    }

    // --- iCloud recovery (spec 21) ----------------------------------------------------------------

    /// <summary>
    /// A fresh install (onboarding not yet completed) looks in the user's iCloud for a copy of the
    /// diary and, finding one, asks before onboarding whether to restore it. Nothing is restored
    /// without the tap, and a phone with no copy sees nothing of this: the search waits a few seconds
    /// for iCloud's index at most.
    /// </summary>
    private async Task OfferCloudRecoveryAsync(Window window)
    {
        if (Preferences.Get(OnboardingCompletedKey, false)) return;

        var services = window.Handler?.MauiContext?.Services;
        var navigation = window.Page?.Navigation;
        var recovery = services?.GetService<Diarion.Services.Recovery.ICloudRecoveryService>();
        if (services == null || navigation == null || recovery is null || !recovery.IsSupported) return;

        Diarion.Services.Recovery.RecoveryCopyInfo? copy;
        try
        {
            copy = await Task.Run(async () =>
                await recovery.IsAvailableAsync()
                    ? await recovery.FindCopyAsync(TimeSpan.FromSeconds(6))
                    : null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"iCloud recovery search failed: {ex}");
            return;
        }
        if (copy is null) return;

        var page = services.GetService<Diarion.Views.RecoveryPage>();
        if (page == null) return;

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.ViewModel.Prepare(copy);
        page.ViewModel.Finished = restored => finished.TrySetResult(restored);

        await navigation.PushModalAsync(page);
        var restored = await finished.Task;
        await navigation.PopModalAsync();

        if (restored)
        {
            // The diary is back; onboarding was done on the phone it came from.
            Preferences.Set(OnboardingCompletedKey, true);
        }
    }

    /// <summary>
    /// The one-time question for a phone that has not answered it (spec 21, D2): asked on the first
    /// launch of a build that carries the feature, once onboarding is behind and no lock is up. "Not
    /// now" is remembered; the switch in Settings → Data remains for later.
    /// </summary>
    private async Task AskCloudRecoveryConsentAsync(Window window)
    {
        if (_isLockShown || !Preferences.Get(OnboardingCompletedKey, false)) return;
        await AskCloudRecoveryConsentAsync(window.Handler?.MauiContext?.Services);
    }

    private static async Task AskCloudRecoveryConsentAsync(IServiceProvider? services)
    {
        var recovery = services?.GetService<Diarion.Services.Recovery.ICloudRecoveryService>();
        var dialogs = services?.GetService<IDialogService>();
        if (recovery is null || dialogs is null || !recovery.NeedsDecision) return;

        try
        {
            if (!await Task.Run(() => recovery.IsAvailableAsync())) return;

            var yes = await dialogs.ShowConfirmationAsync(
                Diarion.Resources.Localization.AppResources.CloudRecoveryConsentTitle,
                Diarion.Resources.Localization.AppResources.CloudRecoveryConsentText,
                Diarion.Resources.Localization.AppResources.CloudRecoveryConsentAccept,
                Diarion.Resources.Localization.AppResources.CloudRecoveryConsentLater);

            if (yes)
            {
                _ = Task.Run(() => recovery.EnableAsync());
            }
            else
            {
                recovery.Decline();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"iCloud recovery consent failed: {ex}");
        }
    }

    /// <summary>The daily copy, on the same launch hook and after the same pause as the autocopies.</summary>
    private static void StartCloudRecoveryCopy(IServiceProvider? services)
    {
        var recovery = services?.GetService<Diarion.Services.Recovery.ICloudRecoveryService>();
        if (recovery is null || !recovery.IsSupported) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            try
            {
                await recovery.RunIfDueAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iCloud recovery copy failed: {ex}");
            }
        });
    }

    /// <summary>A widget tap that arrived while the lock was up is played once the lock is gone.</summary>
    private static async Task PopLockAndFlushAsync(INavigation navigation, IServiceProvider services)
    {
        await navigation.PopModalAsync();
        services.GetService<IDeepLinkService>()?.FlushPending();
    }

    private async Task CheckSecurityAsync(Window window, bool coldStart)
    {
        var services = window.Handler?.MauiContext?.Services;
        if (services == null) return;

        var lockService = services.GetService<IAppLockService>();
        if (lockService == null || !lockService.IsLockEnabled) return;

        // Re-lock policy: always on cold start; on resume only after the background grace elapses.
        var backgroundedTooLong = _backgroundedAtUtc is { } t && (DateTime.UtcNow - t) > RelockGrace;
        if (!coldStart && !backgroundedTooLong) return;

        if (_isLockShown) return;

        var navigation = window.Page?.Navigation;
        if (navigation == null) return;
        if (navigation.ModalStack.LastOrDefault() is Diarion.Views.LockPage) return;

        var lockPage = services.GetService<Diarion.Views.LockPage>();
        if (lockPage == null) return;

        _isLockShown = true;
        lockPage.ViewModel.Unlocked = () =>
        {
            _isLockShown = false;
            _backgroundedAtUtc = null;
            _ = PopLockAndFlushAsync(navigation, services);
        };

        await navigation.PushModalAsync(lockPage);
    }
}
