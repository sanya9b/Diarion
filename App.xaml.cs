using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Diarion.Diagnostics;
using Diarion.Services;
using Diarion.Services.Store;
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

        // Routes that arrive from outside — the home-screen shortcut, a tap on the evening reminder —
        // wait behind the lock screen and are flushed at each point below where the shell is reachable.
        LaunchActions.IsBlocked = () => _isLockShown;
        WireLaunchActions();

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
                        LaunchActions.TryFlush();
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
            await CheckOnboardingAsync(window);
            await CheckSecurityAsync(window, coldStart: true);
            RetiredModelFiles.Sweep();
            LaunchActions.TryFlush();
            StartScheduledBackup(services);
        };
        window.Resumed += async (s, e) =>
        {
            await CheckSecurityAsync(window, coldStart: false);
            LaunchActions.TryFlush();
        };

        return window;
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
        WeakReferenceMessenger.Default.Send(new PaywallRequestedMessage(PaywallReason.AfterOnboarding));
        // After the paywall has had its say: the flush steps aside for any modal, so if the sheet
        // is up this waits for the next resume rather than stacking under it.
        LaunchActions.TryFlush();
    }

    /// <summary>
    /// The two ways into the quick entry from outside the app (roadmap 2.1): the home-screen
    /// shortcut, and a tap on the daily reminder. Neither reads anything outside the process and
    /// neither shows anything past the lock, which is what keeps them on the right side of the
    /// line the roadmap drew against widgets.
    /// </summary>
    private static void WireLaunchActions()
    {
#if ANDROID || IOS
        try
        {
            if (AppActions.Current.IsSupported)
            {
                AppActions.Current.AppActionActivated -= OnAppActionActivated;
                AppActions.Current.AppActionActivated += OnAppActionActivated;
                _ = PublishAppActionsAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App actions unavailable: {ex.Message}");
        }
#endif
#if ANDROID || IOS || MACCATALYST
        Plugin.LocalNotification.LocalNotificationCenter.Current.NotificationActionTapped -= OnNotificationActionTapped;
        Plugin.LocalNotification.LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;
#endif
    }

#if ANDROID || IOS
    private static async Task PublishAppActionsAsync()
    {
        try
        {
            await AppActions.Current.SetAsync(new[]
            {
                new AppAction(
                    LaunchActions.QuickCaptureActionId,
                    Diarion.Resources.Localization.AppResources.QuickCaptureActionTitle)
            });
        }
        catch (Exception ex)
        {
            // A launcher that does not do shortcuts is not an error worth a crash report.
            System.Diagnostics.Debug.WriteLine($"App actions not published: {ex.Message}");
        }
    }

    private static void OnAppActionActivated(object? sender, AppActionEventArgs e)
    {
        if (e.AppAction.Id == LaunchActions.QuickCaptureActionId)
        {
            LaunchActions.Request(Diarion.ViewModels.QuickCaptureViewModel.Route);
        }
    }
#endif

#if ANDROID || IOS || MACCATALYST
    private static void OnNotificationActionTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
    {
        if (e.IsDismissed) return;

        if (e.Request?.NotificationId == LocalNotificationService.DailyReminderId)
        {
            LaunchActions.Request(Diarion.ViewModels.QuickCaptureViewModel.Route);
        }
    }
#endif

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
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await navigation.PopModalAsync();
                LaunchActions.TryFlush();
            });
        };

        await navigation.PushModalAsync(lockPage);
    }
}
