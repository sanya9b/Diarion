using Microsoft.Extensions.DependencyInjection;
using Diarion.Diagnostics;
using Microsoft.Maui.Storage;

namespace Diarion;

public partial class App : Application
{
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
        
        window.Created += async (s, e) => await CheckSecurityAsync(window);
        window.Resumed += async (s, e) => await CheckSecurityAsync(window);

		return window;
	}

    private async System.Threading.Tasks.Task CheckSecurityAsync(Window window)
    {
        if (window.Handler?.MauiContext?.Services == null) return;
        
        var profileService = window.Handler.MauiContext.Services.GetService<Diarion.Services.IProfileService>();
        if (profileService == null) return;

        var profile = await profileService.GetUserProfileAsync();
        
        if (profile.IsBiometricAuthEnabled)
        {
            // Set the lock page as the current modal if not already showing
            if (window.Page?.Navigation.ModalStack.Count == 0 || !(window.Page?.Navigation.ModalStack.LastOrDefault() is Diarion.Views.LockPage))
            {
                await window.Page!.Navigation.PushModalAsync(new Diarion.Views.LockPage(() =>
                {
                    window.Page.Navigation.PopModalAsync();
                }));
            }
        }
    }
}