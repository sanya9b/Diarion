using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class CycleStatusViewModel : BaseViewModel
{
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;

    private DateTime _currentDate = DateTime.Today;

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>True when the shown day is one the user marked — drives the toggle.</summary>
    [ObservableProperty]
    private bool _isPeriodToday;

    [ObservableProperty]
    private string _cycleDayText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForecast))]
    private string _nextPeriodText = string.Empty;

    public bool HasForecast => !string.IsNullOrEmpty(NextPeriodText);

    /// <summary>Which data the forecast rests on, spelled out so no number looks more certain than it is.</summary>
    [ObservableProperty]
    private string _basisText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariabilityNote))]
    private string _variabilityNote = string.Empty;

    public bool HasVariabilityNote => !string.IsNullOrEmpty(VariabilityNote);

    [ObservableProperty]
    private bool _isFertileWindowEstimate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoData))]
    private bool _hasData;

    public bool HasNoData => !HasData;

    public string CycleDay { get; private set; } = string.Empty;

    public CycleStatusViewModel(ICycleLogService cycleLogService, IProfileService profileService)
    {
        _cycleLogService = cycleLogService;
        _profileService = profileService;
    }

    public async Task UpdateForDateAsync(DateTime date)
    {
        _currentDate = date.Date;

        var profile = await _profileService.GetUserProfileAsync();
        if (profile?.IsCycleTrackingActive != true)
        {
            IsVisible = false;
            CycleDay = string.Empty;
            return;
        }

        IsVisible = true;

        var history = CycleForecastCalculator.BuildHistory(await _cycleLogService.GetMarkedDatesAsync());
        Apply(CycleForecastCalculator.Describe(history, profile, _currentDate, DateTime.Today), profile);
    }

    private void Apply(CycleForecast forecast, UserProfile profile)
    {
        IsPeriodToday = forecast.IsPeriodDay;
        HasData = forecast.IsAvailable;
        IsFertileWindowEstimate = forecast.IsFertileWindowEstimate;

        if (!forecast.IsAvailable)
        {
            CycleDay = string.Empty;
            CycleDayText = AppResources.CycleNoDataHint;
            NextPeriodText = string.Empty;
            BasisText = string.Empty;
            VariabilityNote = string.Empty;
            return;
        }

        CycleDay = forecast.CycleDay.ToString(CultureInfo.CurrentCulture);
        CycleDayText = string.Format(AppResources.CycleDayFormat, forecast.CycleDay);

        NextPeriodText = forecast.DaysLate > 0
            ? string.Format(AppResources.CycleLateFormat, forecast.DaysLate)
            : BuildNextPeriodText(forecast);

        BasisText = forecast.Basis switch
        {
            CycleForecastBasis.Averaged => string.Format(
                AppResources.CycleBasisAveragedFormat,
                forecast.RecordedCycleCount,
                Math.Round(forecast.AverageCycleLength).ToString("0", CultureInfo.CurrentCulture)),
            CycleForecastBasis.SingleCycle => AppResources.CycleBasisSingleCycle,
            _ => string.Format(AppResources.CycleBasisProfileDefaultFormat, profile.GetNormalizedCycleLength())
        };

        VariabilityNote = forecast.IsHighVariability ? AppResources.CycleHighVariabilityNote : string.Empty;
    }

    private static string BuildNextPeriodText(CycleForecast forecast)
    {
        if (forecast.PredictedNextStart is not DateTime start) return string.Empty;

        var text = string.Format(AppResources.CycleNextPeriodFormat, start.ToString("d MMM", CultureInfo.CurrentCulture));
        if (forecast.UncertaintyDays > 0)
        {
            text += " " + string.Format(AppResources.CycleUncertaintyFormat, forecast.UncertaintyDays);
        }

        return text;
    }

    [RelayCommand]
    private async Task TogglePeriodDayAsync()
    {
        if (!IsVisible) return;

        await _cycleLogService.ToggleAsync(_currentDate);
        await UpdateForDateAsync(_currentDate);

        WeakReferenceMessenger.Default.Send(new CycleLogChangedMessage(_currentDate));
    }
}
