using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// Resolves the cycle day for whichever day the diary is showing. Nothing on the day screen renders it —
/// the cycle lives on its own settings page, because a toggle that matters five days a month has no
/// business taking up room on the other twenty-five. This exists so the entry still carries the day
/// number, and so the calendar knows tracking is on.
/// </summary>
public partial class CycleStatusViewModel : BaseViewModel
{
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private bool _isVisible;

    public string CycleDay { get; private set; } = string.Empty;

    public CycleStatusViewModel(ICycleLogService cycleLogService, IProfileService profileService)
    {
        _cycleLogService = cycleLogService;
        _profileService = profileService;
    }

    public async Task UpdateForDateAsync(DateTime date)
    {
        var profile = await _profileService.GetUserProfileAsync();
        if (profile?.IsCycleTrackingActive != true)
        {
            IsVisible = false;
            CycleDay = string.Empty;
            return;
        }

        IsVisible = true;

        var history = CycleForecastCalculator.BuildHistory(await _cycleLogService.GetMarkedDatesAsync());
        var forecast = CycleForecastCalculator.Describe(history, profile, date.Date, DateTime.Today);

        CycleDay = forecast.IsAvailable ? forecast.CycleDay.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }
}
