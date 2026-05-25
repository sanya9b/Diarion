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

        // Відновлюємо тему з налаштувань
        var savedTheme = Preferences.Get("AppTheme", "System");
        if (savedTheme == "Light")
            UserAppTheme = AppTheme.Light;
        else if (savedTheme == "Dark")
            UserAppTheme = AppTheme.Dark;
        else
            UserAppTheme = AppTheme.Unspecified;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		using var _ = StartupTrace.Measure("App.CreateWindow");
		return new Window(new AppShell());
	}
}