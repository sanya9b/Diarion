using System;
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
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>
/// Recording a period from the home screen, in a bubble that opens from the health block's drop — the
/// same gesture as the hour picker on the mood card. The first and the last day are both chosen, so a
/// short or a long period is recorded as it was rather than stretched to the profile's default length;
/// the last day follows the first until the user picks it. Under the dates, the symptoms of the day the
/// home screen shows: the same eight as on the cycle page, each saved as it is tapped.
/// </summary>
public partial class CycleRecordViewModel : ObservableObject
{
    /// <summary>A longer run is a mistyped date, not a period; the save button stays off.</summary>
    public const int MaxPeriodDays = 15;

    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;

    private int _periodLength = 5;

    // The last day follows the first until the user moves it; after that it is theirs.
    private bool _endChosen;
    private bool _settingEnd;

    public CycleRecordViewModel(ICycleLogService cycleLogService, IProfileService profileService)
    {
        _cycleLogService = cycleLogService;
        _profileService = profileService;
    }

    /// <summary>Whether the bubble is out. View state, kept here so it is testable.</summary>
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Days), nameof(DaysText), nameof(CanSave))]
    private DateTime _start = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Days), nameof(DaysText), nameof(CanSave))]
    private DateTime _end = DateTime.Today;

    /// <summary>Nothing after today can be recorded; the service ignores future days anyway.</summary>
    public DateTime MaximumDate => DateTime.Today;

    /// <summary>Both ends included: a period that started and ended today is one day.</summary>
    public int Days => (End.Date - Start.Date).Days + 1;

    public string DaysText => string.Format(CultureInfo.CurrentCulture, AppResources.CycleRecordDaysFormat, Days);

    public bool CanSave => Days >= 1 && Days <= MaxPeriodDays && End.Date <= DateTime.Today;

    /// <summary>The most recent recorded period, so the user can see what is already there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastRecorded))]
    private string _lastRecordedText = string.Empty;

    public bool HasLastRecorded => !string.IsNullOrEmpty(LastRecordedText);

    /// <summary>The symptom chips for <see cref="SymptomDay"/>, filled when the bubble opens.</summary>
    public ObservableCollection<SymptomToggleViewModel> Symptoms { get; } = new();

    /// <summary>The day the home screen shows; its symptoms are the ones the chips read and write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SymptomsTitle), nameof(CanLogSymptoms))]
    private DateTime _symptomDay = DateTime.Today;

    /// <summary>"Symptoms today", or the date when the home screen shows another day.</summary>
    public string SymptomsTitle => SymptomDay.Date == DateTime.Today
        ? AppResources.CycleSymptomsToday
        : string.Format(CultureInfo.CurrentCulture, AppResources.CycleSymptomsOnDayFormat,
            SymptomDay.ToString("d MMM", CultureInfo.CurrentCulture));

    /// <summary>A day that has not happened has no symptoms to note; the cycle page stops at today too.</summary>
    public bool CanLogSymptoms => SymptomDay.Date <= DateTime.Today;

    partial void OnStartChanged(DateTime value)
    {
        if (!_endChosen || End.Date < value.Date)
        {
            SetEndQuietly(DefaultEndFor(value));
        }
    }

    partial void OnEndChanged(DateTime value)
    {
        if (!_settingEnd) _endChosen = true;
    }

    /// <summary>
    /// The drop button: opens the bubble — the dates on today, the symptoms on <paramref name="day"/>, the
    /// day the home screen shows (today when not given) — or puts it away if it is out.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync(DateTime? day)
    {
        if (IsOpen)
        {
            IsOpen = false;
            return;
        }

        var profile = await _profileService.GetUserProfileAsync() ?? new UserProfile();
        _periodLength = profile.GetNormalizedPeriodLength();

        _endChosen = false;
        Start = DateTime.Today;
        SetEndQuietly(DefaultEndFor(Start));
        await RefreshLastRecordedAsync();

        SymptomDay = (day ?? DateTime.Today).Date;
        await LoadSymptomsAsync();

        IsOpen = true;
    }

    /// <summary>A symptom chip: saved at once, like on the cycle page. The check button is for the dates only.</summary>
    [RelayCommand]
    private async Task ToggleSymptomAsync(SymptomToggleViewModel? toggle)
    {
        if (toggle == null || !CanLogSymptoms) return;

        toggle.IsSelected = !toggle.IsSelected;
        var written = await WriteGate.TryAsync(() => _cycleLogService.SetSymptomsAsync(
            SymptomDay, Symptoms.Where(s => s.IsSelected).Select(s => s.Key)));

        // Not written (the diary is read-only): the chip shows what is actually on record.
        if (!written) toggle.IsSelected = !toggle.IsSelected;
    }

    private async Task LoadSymptomsAsync()
    {
        var day = SymptomDay.Date;
        var logged = (await _cycleLogService.GetLogsAsync())
            ?.FirstOrDefault(l => l.Date.Date == day)?.Symptoms ?? new System.Collections.Generic.List<string>();

        Symptoms.Clear();
        foreach (var key in CycleSymptoms.All)
        {
            Symptoms.Add(SymptomToggleViewModel.For(key, logged.Contains(key)));
        }
    }

    /// <summary>A tap outside the bubble or a scroll: nothing is recorded.</summary>
    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave) return;

        if (!await WriteGate.TryAsync(() => _cycleLogService.AddEpisodeAsync(Start.Date, Days))) return;

        // Recording one period moves every later prediction, so the calendar and the day screen repaint.
        WeakReferenceMessenger.Default.Send(new CycleLogChangedMessage(DateTime.Today));
        await RefreshLastRecordedAsync();
        IsOpen = false;
    }

    /// <summary>
    /// The profile's period length from the chosen first day, but never past today: an ongoing period
    /// is recorded up to today and extended by recording it again later.
    /// </summary>
    private DateTime DefaultEndFor(DateTime start)
    {
        var end = start.Date.AddDays(Math.Max(1, _periodLength) - 1);
        return end > DateTime.Today ? DateTime.Today : end;
    }

    private void SetEndQuietly(DateTime value)
    {
        _settingEnd = true;
        try
        {
            End = value < Start.Date ? Start.Date : value;
        }
        finally
        {
            _settingEnd = false;
        }
    }

    private async Task RefreshLastRecordedAsync()
    {
        var history = CycleForecastCalculator.BuildHistory(await _cycleLogService.GetMarkedDatesAsync());
        var last = history.Episodes.OrderByDescending(e => e.Start).FirstOrDefault();

        LastRecordedText = last is null
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, AppResources.CycleRecordLastFormat,
                CycleViewModel.FormatRange(last.Start, last.End));
    }
}
