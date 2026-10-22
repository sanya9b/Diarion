using System.Globalization;
using System.Threading.Tasks;
using Diarion.Models;
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

    public ProfilePage(ProfileViewModel viewModel, SubscriptionSectionViewModel subscription, MoodIconSetViewModel moodIconSets)
    {
        InitializeComponent();
        SubscriptionCard.BindingContext = subscription;
        MoodIconSetRow.BindingContext = moodIconSets;

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

        TourOverlay.AnchorResolver = AnchorFor;
        TourOverlay.PrepareStepAsync = PrepareTourStepAsync;
        TourOverlay.ScrollHost = PageScroll;
        TourOverlay.Tour = _viewModel.Tour;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadProfileAsync();

        // After the profile: the guide is about the switches on this screen, and until it is loaded
        // they are showing defaults.
        await _viewModel.Tour.StartAsync(TourId.Settings);
    }

    // --- Settings guide (onboarding stage 3) ------------------------------------------------------
    // TourOverlayView does the drawing. This half answers what only the page can: which element an
    // id means, and what has to be on screen before it can be measured.

    private Task PrepareTourStepAsync(TourStep step)
    {
        // Two of the four targets live on the "Screen" tab, and the guide runs over whichever tab was
        // left open. The step that points at the tab bar deliberately does not switch: the first
        // thing it says is that these tabs exist, and moving one under the user while saying so is
        // worse than starting wherever they are.
        if (step.Id is TourStepId.SettingsBlocks or TourStepId.SettingsPrompts)
        {
            _viewModel.SelectedTabIndex = ScreenTabIndex;
        }

        return Task.CompletedTask;
    }

    private const int ScreenTabIndex = 1;

    private VisualElement? AnchorFor(TourStepId id) => id switch
    {
        TourStepId.SettingsTabs => TabsAnchor,
        TourStepId.SettingsBlocks => BlocksCardAnchor,
        TourStepId.SettingsPrompts => PromptsCardAnchor,
        TourStepId.SettingsSave => SaveAnchor,
        _ => null
    };

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
