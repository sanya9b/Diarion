using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Diarion.Diagnostics;
using Diarion.Services;
using Diarion.Services.Ai;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Diarion;

public partial class App : Application
{
    private const string OnboardingCompletedKey = "OnboardingCompleted";
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
                    MainThread.BeginInvokeOnMainThread(() => window.Page = new AppShell());
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
            await CheckOnboardingAsync(window);
            await CheckSecurityAsync(window, coldStart: true);
        };
        window.Resumed += async (s, e) =>
        {
            await CheckSecurityAsync(window, coldStart: false);
            StartEmbeddingIndex(window.Handler?.MauiContext?.Services);
        };

        return window;
    }

    /// <summary>
    /// Nudges the embedding index after the UI is up. It no-ops when AI is off or no model is
    /// installed, and it derives its own work queue, so calling it repeatedly is free.
    /// </summary>
    private static void StartEmbeddingIndex(IServiceProvider? services) =>
        (services ?? IPlatformApplication.Current?.Services)?.GetService<IEmbeddingIndexService>()?.Start();

    private async Task CheckOnboardingAsync(Window window)
    {
        if (Preferences.Get(OnboardingCompletedKey, false)) return;

        var services = window.Handler?.MauiContext?.Services;
        var navigation = window.Page?.Navigation;
        if (services == null || navigation == null) return;

        var page = services.GetService<Diarion.Views.OnboardingPage>();
        if (page == null) return;

        page.ViewModel.Completed = () =>
        {
            Preferences.Set(OnboardingCompletedKey, true);
            _ = navigation.PopModalAsync();
        };

        await navigation.PushModalAsync(page);
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _backgroundedAtUtc = DateTime.UtcNow;

        // Android gives no dependable background CPU, and a foreground service is not worth the
        // permission surface for indexing. Stopping also has to happen before the database can be
        // reopened by a backup restore, which would invalidate the collection the loop writes to.
        var services = IPlatformApplication.Current?.Services;
        _ = services?.GetService<IEmbeddingIndexService>()?.StopAsync();

        // The graph is tens of megabytes resident and nothing needs it while the user is elsewhere.
        (services?.GetService<ITextEmbedder>() as OnnxTextEmbedder)?.Unload();
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
            _ = navigation.PopModalAsync();
        };

        await navigation.PushModalAsync(lockPage);
    }
}
