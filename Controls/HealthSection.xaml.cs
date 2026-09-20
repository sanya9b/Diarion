using Microsoft.Maui.Controls;

namespace Diarion.Controls;

public partial class HealthSection : ContentView
{
    /// <summary>Whether the intimate-life row is shown. Decided by the profile's age gate, set by the page.</summary>
    public static readonly BindableProperty IsIntimateLifeAvailableProperty = BindableProperty.Create(
        nameof(IsIntimateLifeAvailable), typeof(bool), typeof(HealthSection), false);

    public bool IsIntimateLifeAvailable
    {
        get => (bool)GetValue(IsIntimateLifeAvailableProperty);
        set => SetValue(IsIntimateLifeAvailableProperty, value);
    }

    /// <summary>Whether the one-line "set your date of birth" hint takes the row's place.</summary>
    public static readonly BindableProperty ShowsIntimateLifeAgeHintProperty = BindableProperty.Create(
        nameof(ShowsIntimateLifeAgeHint), typeof(bool), typeof(HealthSection), false);

    public bool ShowsIntimateLifeAgeHint
    {
        get => (bool)GetValue(ShowsIntimateLifeAgeHintProperty);
        set => SetValue(ShowsIntimateLifeAgeHintProperty, value);
    }

    public HealthSection()
    {
        InitializeComponent();
    }
}
