using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class CycleStatusViewModel : BaseViewModel
{
    private readonly IMenstrualCycleService _menstrualCycleService;
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private string _cycleDayText = string.Empty;

    [ObservableProperty]
    private string _pregnancyProbabilityText = string.Empty;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private Microsoft.Maui.Graphics.Color _pregnancyProbabilityColor = Microsoft.Maui.Graphics.Colors.Transparent;
    
    public string CycleDay { get; private set; } = string.Empty;

    public CycleStatusViewModel(IMenstrualCycleService menstrualCycleService, IProfileService profileService)
    {
        _menstrualCycleService = menstrualCycleService;
        _profileService = profileService;
    }

    public async Task UpdateForDateAsync(DateTime date)
    {
        var profile = await _profileService.GetUserProfileAsync();
        var cycleInfo = _menstrualCycleService.GetCycleInfoForDate(date, profile);

        if (cycleInfo.IsTrackingEnabled)
        {
            IsVisible = true;
            CycleDay = cycleInfo.DayOfCycle.ToString();
            CycleDayText = string.Format(Diarion.Resources.Localization.AppResources.CycleDayFormat, cycleInfo.DayOfCycle);

            switch (cycleInfo.Probability)
            {
                case PregnancyProbability.High:
                    PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbHigh;
                    PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#C26D53");
                    break;
                case PregnancyProbability.Medium:
                    PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbMedium;
                    PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#C9985A");
                    break;
                case PregnancyProbability.Low:
                default:
                    PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbLow;
                    PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#8FA083");
                    break;
            }
        }
        else
        {
            IsVisible = false;
            CycleDay = string.Empty;
        }
    }
}
