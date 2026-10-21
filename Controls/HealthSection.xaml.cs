using System.Windows.Input;
using Diarion.Helpers;
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

    /// <summary>Whether the period chip is shown: cycle tracking on in the profile. Set by the page.</summary>
    public static readonly BindableProperty IsCycleRecordAvailableProperty = BindableProperty.Create(
        nameof(IsCycleRecordAvailable), typeof(bool), typeof(HealthSection), false);

    public bool IsCycleRecordAvailable
    {
        get => (bool)GetValue(IsCycleRecordAvailableProperty);
        set => SetValue(IsCycleRecordAvailableProperty, value);
    }

    /// <summary>
    /// What the period chip does. The page hands in the recording panel's open command; the entry this
    /// block binds to knows nothing about the cycle log, and should not.
    /// </summary>
    public ICommand? RecordCycleCommand { get; set; }

    public HealthSection()
    {
        InitializeComponent();
    }

    /// <summary>Where the period panel grows from: the chip, which its copy covers. Page coordinates; null while hidden.</summary>
    public Rect? GetCycleChipAnchor() =>
        CycleChip.IsVisible && CycleChip.Width > 0 ? VisualTreeGeometry.BoundsOnPage(CycleChip) : null;

    private void OnCycleChipTapped(object? sender, TappedEventArgs e)
    {
        if (RecordCycleCommand?.CanExecute(null) == true) RecordCycleCommand.Execute(null);
    }
}
