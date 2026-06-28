using System.Globalization;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _viewModel;

    public static readonly BindableProperty LangThumbAlignmentProperty = BindableProperty.Create(
        nameof(LangThumbAlignment), typeof(LayoutOptions), typeof(ProfilePage), LayoutOptions.Start);

    public static readonly BindableProperty ThemeThumbAlignmentProperty = BindableProperty.Create(
        nameof(ThemeThumbAlignment), typeof(LayoutOptions), typeof(ProfilePage), LayoutOptions.Start);

    public LayoutOptions LangThumbAlignment
    {
        get => (LayoutOptions)GetValue(LangThumbAlignmentProperty);
        set => SetValue(LangThumbAlignmentProperty, value);
    }

    public LayoutOptions ThemeThumbAlignment
    {
        get => (LayoutOptions)GetValue(ThemeThumbAlignmentProperty);
        set => SetValue(ThemeThumbAlignmentProperty, value);
    }

    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        
        var currentLanguage = Preferences.Get("AppLanguage", "en");
        LangThumbAlignment = currentLanguage == "uk" ? LayoutOptions.End : LayoutOptions.Start;

        var currentTheme = Diarion.Services.ThemeManager.GetCurrentTheme();
        if (currentTheme == Diarion.Services.ThemeManager.ThemePink)
            ThemeThumbAlignment = LayoutOptions.End;
        else if (currentTheme == Diarion.Services.ThemeManager.ThemeDark)
            ThemeThumbAlignment = LayoutOptions.Center;
        else
            ThemeThumbAlignment = LayoutOptions.Start;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadProfileAsync();
    }

    private async void OnToggleThemeClicked(object? sender, EventArgs e)
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

        if (newTheme == Diarion.Services.ThemeManager.ThemePink)
            ThemeThumbAlignment = LayoutOptions.End;
        else if (newTheme == Diarion.Services.ThemeManager.ThemeDark)
            ThemeThumbAlignment = LayoutOptions.Center;
        else
            ThemeThumbAlignment = LayoutOptions.Start;
    }

    private async void OnToggleLanguageClicked(object? sender, EventArgs e)
    {
        var currentLanguage = Preferences.Get("AppLanguage", "en");
        var newCulture = currentLanguage == "uk" ? "en" : "uk";
        Preferences.Set("AppLanguage", newCulture);
        
        var culture = new CultureInfo(newCulture);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Diarion.Resources.Localization.AppResources.Culture = culture;
        Diarion.Resources.Localization.LocalizationResourceManager.Instance.SetCulture(culture);

        if (Application.Current?.Windows.Count > 0)
        {
            Dispatcher.Dispatch(async () =>
            {
                var window = Application.Current.Windows[0];
#if IOS || MACCATALYST
                window.Page = new ContentPage { BackgroundColor = Application.Current.UserAppTheme == AppTheme.Dark ? Colors.Black : Colors.White };
                await Task.Delay(50);
#endif
                window.Page = new AppShell();
            });
        }
    }
}
