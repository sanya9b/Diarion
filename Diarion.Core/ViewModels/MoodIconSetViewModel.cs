using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// The "Mood icons" row in settings (spec 18): three icons, one tap picks a set. The open day hears
/// about it through <see cref="MoodIconSetChangedMessage"/> and redraws its faces at once.
/// </summary>
public partial class MoodIconSetViewModel : ObservableObject
{
    private readonly IMoodIconSetService _service;

    public MoodIconSetViewModel(IMoodIconSetService service)
    {
        _service = service;
        _current = service.Current;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentName))]
    [NotifyPropertyChangedFor(nameof(SelectedIndex))]
    private MoodIconSet _current;

    /// <summary>The chosen set's name, shown under the row's title, since the icons carry no captions.</summary>
    public string CurrentName => Current switch
    {
        MoodIconSet.Weather => AppResources.MoodIconSetWeather,
        MoodIconSet.Plants => AppResources.MoodIconSetPlants,
        _ => AppResources.MoodIconSetEmoji
    };

    /// <summary>The column the switch's thumb sits in: the sets in enum order.</summary>
    public int SelectedIndex => (int)Current;

    [RelayCommand]
    private void Select(MoodIconSet set)
    {
        if (set == Current) return;

        _service.Set(set);
        Current = set;
        WeakReferenceMessenger.Default.Send(new MoodIconSetChangedMessage(set));
    }
}
