using System.Globalization;
using Diarion.Diagnostics;
using Microsoft.Maui.Controls;

namespace Diarion;

public partial class AppShell : Shell
{
    private LayoutOptions _langThumbAlignment;
    public LayoutOptions LangThumbAlignment
    {
        get => _langThumbAlignment;
        set
        {
            _langThumbAlignment = value;
            OnPropertyChanged();
        }
    }

    private LayoutOptions _themeThumbAlignment;
    public LayoutOptions ThemeThumbAlignment
    {
        get => _themeThumbAlignment;
        set
        {
            _themeThumbAlignment = value;
            OnPropertyChanged();
        }
    }

    public AppShell()
    {
        using var _ = StartupTrace.Measure("AppShell..ctor");
        
        // Налаштовуємо позицію бігунка (switch) для мови при старті
        var currentLanguage = Preferences.Get("AppLanguage", "en");
        LangThumbAlignment = currentLanguage == "uk" ? LayoutOptions.End : LayoutOptions.Start;

        // Налаштовуємо позицію бігунка (switch) для теми при старті
        var currentTheme = Diarion.Services.ThemeManager.GetCurrentTheme();
        if (currentTheme == Diarion.Services.ThemeManager.ThemePink)
            ThemeThumbAlignment = LayoutOptions.End;
        else if (currentTheme == Diarion.Services.ThemeManager.ThemeDark)
            ThemeThumbAlignment = LayoutOptions.Center;
        else
            ThemeThumbAlignment = LayoutOptions.Start;

        InitializeComponent();
        StartupTrace.Mark("AppShell.InitializeComponent complete");
        
        Routing.RegisterRoute("DiaryDetail", typeof(Views.DiaryDetailPage));
        Routing.RegisterRoute("TodoDetail", typeof(Views.TodoDetailPage));
        Routing.RegisterRoute("HabitTracker", typeof(Views.HabitTrackerPage));
        Routing.RegisterRoute("ReadingTracker", typeof(Views.ReadingTrackerPage));
        Routing.RegisterRoute("HappyMoments", typeof(Views.HappyMomentsPage));
        Routing.RegisterRoute("GoodDeeds", typeof(Views.GoodDeedsPage));
        Routing.RegisterRoute("Wishlist", typeof(Views.WishlistPage));
        Routing.RegisterRoute("Finance", typeof(Views.FinancePage));
    }

    private async void OnToggleThemeClicked(object? sender, TappedEventArgs e)
    {
        if (Application.Current == null) return;

        var currentTheme = Diarion.Services.ThemeManager.GetCurrentTheme();
        string newTheme;
        
        if (currentTheme == Diarion.Services.ThemeManager.ThemeLight)
            newTheme = Diarion.Services.ThemeManager.ThemeDark;
        else if (currentTheme == Diarion.Services.ThemeManager.ThemeDark)
            newTheme = Diarion.Services.ThemeManager.ThemePink;
        else
            newTheme = Diarion.Services.ThemeManager.ThemeLight;

        Diarion.Services.ThemeManager.SetTheme(newTheme);

        // Оновлюємо UI Thumb
        if (newTheme == Diarion.Services.ThemeManager.ThemePink)
            ThemeThumbAlignment = LayoutOptions.End;
        else if (newTheme == Diarion.Services.ThemeManager.ThemeDark)
            ThemeThumbAlignment = LayoutOptions.Center;
        else
            ThemeThumbAlignment = LayoutOptions.Start;

        Current.FlyoutIsPresented = false;
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
        Diarion.Resources.Localization.LocalizationResourceManager.Instance.SetCulture(culture);

        // Закриваємо меню перед перестворенням сторінки
        Current.FlyoutIsPresented = false;
        await Task.Delay(250); // Чекаємо завершення анімації закриття меню

        // Перезавантажуємо AppShell, щоб оновити всі x:Static Binding-и
        if (Application.Current?.Windows.Count > 0)
        {
            Dispatcher.Dispatch(async () =>
            {
                var window = Application.Current.Windows[0];
#if IOS || MACCATALYST
                // На iOS заміна AppShell на новий AppShell може викликати краш. 
                // Спочатку ставимо порожню сторінку, щоб коректно вивантажити старий Shell.
                window.Page = new ContentPage { BackgroundColor = Application.Current.UserAppTheme == AppTheme.Dark ? Colors.Black : Colors.White };
                await Task.Delay(50);
#endif
                window.Page = new AppShell();
            });
        }
    }
}
