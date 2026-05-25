using System.Globalization;
using Diarion.Diagnostics;
using Microsoft.Maui.Controls;

namespace Diarion;

public partial class AppShell : Shell
{
	public AppShell()
	{
        using var _ = StartupTrace.Measure("AppShell..ctor");
		InitializeComponent();
        StartupTrace.Mark("AppShell.InitializeComponent complete");
		Routing.RegisterRoute("DiaryDetail", typeof(Views.DiaryDetailPage));
        Routing.RegisterRoute("TodoDetail", typeof(Views.TodoDetailPage));

        // Налаштовуємо позицію бігунка (switch) для мови при старті
        var currentLanguage = Preferences.Get("AppLanguage", "en");
        LangThumb.HorizontalOptions = currentLanguage == "uk" ? LayoutOptions.End : LayoutOptions.Start;
	}

    private async void OnToggleThemeClicked(object? sender, TappedEventArgs e)
    {
        if (Application.Current == null) return;

        var currentTheme = Application.Current.UserAppTheme == AppTheme.Unspecified 
            ? Application.Current.PlatformAppTheme 
            : Application.Current.UserAppTheme;
            
        var newTheme = currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Application.Current.UserAppTheme = newTheme;

        // Зберігаємо вибір користувача
        Preferences.Set("AppTheme", newTheme == AppTheme.Dark ? "Dark" : "Light");

        // Закриваємо меню перед перестворенням сторінки
        Current.FlyoutIsPresented = false;
        await Task.Delay(250); // Чекаємо завершення анімації закриття меню

        // Іноді AppThemeBinding може не оновити всі глибокі елементи (наприклад в CollectionView), 
        // тому перезавантаження сторінки гарантує ідеальне застосування нової теми.
        if (Application.Current?.Windows.Count > 0)
        {
            Dispatcher.Dispatch(() =>
            {
                Application.Current.Windows[0].Page = new AppShell();
            });
        }
    }

    private async void OnToggleLanguageClicked(object? sender, TappedEventArgs e)
    {
        // На Android Thread.CurrentUICulture може скидатись системою, тому надійніше брати зі збережених налаштувань
        var currentLanguage = Preferences.Get("AppLanguage", "en");
        
        // Перемикаємо між англійською (en) та українською (uk)
        var newCulture = currentLanguage == "uk" ? "en" : "uk";
        
        // Зберігаємо мову
        Preferences.Set("AppLanguage", newCulture);
        
        var culture = new CultureInfo(newCulture);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Diarion.Resources.Localization.AppResources.Culture = culture;

        // Закриваємо меню перед перестворенням сторінки
        Current.FlyoutIsPresented = false;
        await Task.Delay(250); // Чекаємо завершення анімації закриття меню

        // Перезавантажуємо AppShell, щоб оновити всі x:Static Binding-и
        if (Application.Current?.Windows.Count > 0)
        {
            Dispatcher.Dispatch(() =>
            {
                Application.Current.Windows[0].Page = new AppShell();
            });
        }
    }
}
