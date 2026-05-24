using System.Globalization;
using Microsoft.Maui.Controls;

namespace Diarion;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("DiaryDetail", typeof(Views.DiaryDetailPage));
	}

    private void OnToggleThemeClicked(object? sender, EventArgs e)
    {
        if (Application.Current == null) return;

        var currentTheme = Application.Current.UserAppTheme == AppTheme.Unspecified 
            ? Application.Current.RequestedTheme 
            : Application.Current.UserAppTheme;
            
        Application.Current.UserAppTheme = currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    }

    private void OnToggleLanguageClicked(object? sender, EventArgs e)
    {
        var currentCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        // Перемикаємо між англійською (en) та українською (uk)
        var newCulture = currentCulture == "uk" ? "en" : "uk";
        
        var culture = new CultureInfo(newCulture);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Перезавантажуємо AppShell, щоб оновити всі x:Static Binding-и
        if (Application.Current?.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page = new AppShell();
        }
    }
}
