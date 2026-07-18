using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Diarion.Diagnostics;
using Diarion.Services;
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

        InitializeComponent();

        // Відновлюємо тему з налаштувань за допомогою ThemeManager
        var currentTheme = Diarion.Services.ThemeManager.GetCurrentTheme();
        Diarion.Services.ThemeManager.SetTheme(currentTheme);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        using var _ = StartupTrace.Measure("App.CreateWindow");
        var window = new Window(new AppShell());

        window.Created += async (s, e) =>
        {
            await CheckOnboardingAsync(window);
            await CheckSecurityAsync(window, coldStart: true);
        };
        window.Resumed += async (s, e) => await CheckSecurityAsync(window, coldStart: false);

        return window;
    }

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
