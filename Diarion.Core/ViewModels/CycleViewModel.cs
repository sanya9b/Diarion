using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>One recorded period as the list shows it.</summary>
public class CycleEpisodeItemViewModel
{
    public DateTime Start { get; init; }
    public string RangeText { get; init; } = string.Empty;

    /// <summary>Days to the next period's start, or empty for the most recent one.</summary>
    public string IntervalText { get; init; } = string.Empty;
}

/// <summary>One symptom chip. Carries its own selection flag because the page highlights with DataTriggers.</summary>
public partial class SymptomToggleViewModel : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class CycleViewModel : BaseViewModel
{
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    private UserProfile _profile = new();

    public ObservableCollection<CycleEpisodeItemViewModel> Episodes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoData))]
    private bool _hasData;

    public bool HasNoData => !HasData;

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

    /// <summary>Start date for the "record a period" picker; today, never the future.</summary>
    [ObservableProperty]
    private DateTime _newEpisodeStart = DateTime.Today;

    public DateTime MaximumDate => DateTime.Today;

    public CycleViewModel(
        ICycleLogService cycleLogService,
        IProfileService profileService,
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _cycleLogService = cycleLogService;
        _profileService = profileService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = AppResources.CycleTitle;
    }

    public async Task LoadAsync()
    {
        _profile = await _profileService.GetUserProfileAsync() ?? new UserProfile();

        var history = CycleForecastCalculator.BuildHistory(await _cycleLogService.GetMarkedDatesAsync());
        var forecast = CycleForecastCalculator.Describe(history, _profile, DateTime.Today, DateTime.Today);

        BuildEpisodeList(history);
        ApplyForecast(forecast);
        await LoadSymptomsAsync();
    }

    // --- Symptom log ---

    public ObservableCollection<SymptomToggleViewModel> SymptomToggles { get; } = new();

    [ObservableProperty]
    private DateTime _symptomDate = DateTime.Today;

    partial void OnSymptomDateChanged(DateTime value) => _ = LoadSymptomsAsync();

    private async Task LoadSymptomsAsync()
    {
        var day = SymptomDate.Date;
        var logged = (await _cycleLogService.GetLogsAsync())
            .FirstOrDefault(l => l.Date.Date == day)?.Symptoms ?? new List<string>();

        SymptomToggles.Clear();
        foreach (var key in CycleSymptoms.All)
        {
            SymptomToggles.Add(new SymptomToggleViewModel
            {
                Key = key,
                Label = Diarion.Resources.Localization.AppResources.ResourceManager
                            .GetString(key, Diarion.Resources.Localization.AppResources.Culture
                                            ?? System.Globalization.CultureInfo.CurrentUICulture) ?? key,
                IsSelected = logged.Contains(key)
            });
        }
    }

    [RelayCommand]
    private async Task ToggleSymptomAsync(SymptomToggleViewModel? toggle)
    {
        if (toggle == null) return;

        toggle.IsSelected = !toggle.IsSelected;
        await _cycleLogService.SetSymptomsAsync(
            SymptomDate, SymptomToggles.Where(t => t.IsSelected).Select(t => t.Key));
    }

    private void BuildEpisodeList(CycleHistory history)
    {
        Episodes.Clear();

        // Newest first: the recent cycles are the ones worth correcting.
        var ordered = history.Episodes.OrderByDescending(e => e.Start).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var episode = ordered[i];
            var next = i > 0 ? ordered[i - 1] : null;

            Episodes.Add(new CycleEpisodeItemViewModel
            {
                Start = episode.Start,
                RangeText = episode.Start == episode.End
                    ? episode.Start.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                    : $"{episode.Start.ToString("d MMM", CultureInfo.CurrentCulture)} – {episode.End.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}",
                IntervalText = next == null
                    ? string.Empty
                    : string.Format(AppResources.CycleIntervalFormat, (next.Start - episode.Start).Days)
            });
        }
    }

    private void ApplyForecast(CycleForecast forecast)
    {
        HasData = forecast.IsAvailable;
        IsFertileWindowEstimate = forecast.IsFertileWindowEstimate;

        if (!forecast.IsAvailable)
        {
            CycleDayText = string.Empty;
            NextPeriodText = string.Empty;
            BasisText = string.Empty;
            VariabilityNote = string.Empty;
            return;
        }

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
            _ => string.Format(AppResources.CycleBasisProfileDefaultFormat, _profile.GetNormalizedCycleLength())
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
    private async Task AddEpisodeAsync()
    {
        await _cycleLogService.AddEpisodeAsync(NewEpisodeStart, _profile.GetNormalizedPeriodLength());
        await ReloadAndBroadcastAsync();
    }

    [RelayCommand]
    private async Task DeleteEpisodeAsync(CycleEpisodeItemViewModel? item)
    {
        if (item == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            AppResources.CycleDeleteConfirmTitle,
            string.Format(AppResources.CycleDeleteConfirmMessage, item.RangeText),
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);

        if (!confirm) return;

        await _cycleLogService.RemoveEpisodeAsync(item.Start);
        await ReloadAndBroadcastAsync();
    }

    private async Task ReloadAndBroadcastAsync()
    {
        await LoadAsync();

        // Editing one period moves every later prediction, so the calendar repaints wholesale.
        WeakReferenceMessenger.Default.Send(new CycleLogChangedMessage(DateTime.Today));
    }

    [RelayCommand]
    private async Task CloseAsync() => await _navigationService.NavigateBackAsync();
}
