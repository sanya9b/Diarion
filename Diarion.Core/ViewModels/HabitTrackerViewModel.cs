using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class HabitTrackerViewModel : BaseViewModel
{
    private readonly IHabitService _habitService;

    public ObservableCollection<HarmfulHabitTrackerItemViewModel> Trackers { get; } = new();
    public ObservableCollection<HarmfulHabitDayViewModel> TrackerDays { get; } = new();

    [ObservableProperty]
    private string _newTrackerName = string.Empty;

    [ObservableProperty]
    private DateTime _newTrackerStartDate = DateTime.Today;

    [ObservableProperty]
    private string _newTrackerCost = string.Empty;

    [ObservableProperty]
    private string _newTrackerUnits = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitleText))]
    [NotifyPropertyChangedFor(nameof(FormButtonText))]
    private bool _isEditingTracker;

    private Guid _editingTrackerId;

    public string FormTitleText => IsEditingTracker ? AppResources.HabitTrackerEditTitle : AppResources.HabitTrackerSetupTitle;
    public string FormButtonText => IsEditingTracker ? AppResources.HabitTrackerSaveButton : AppResources.HabitTrackerAddButton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTracker))]
    private HarmfulHabitTrackerItemViewModel? _selectedTracker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isAddTrackerFormVisible;

    private readonly IDialogService _dialogService;

    public HabitTrackerViewModel(IHabitService habitService, IDialogService dialogService)
    {
        _habitService = habitService;
        _dialogService = dialogService;
        Title = AppResources.HabitTrackerTitle;
    }

    public bool HasTrackers => Trackers.Count > 0;
    public bool HasNoTrackers => !HasTrackers;
    public bool HasSelectedTracker => SelectedTracker != null;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public DateTime MaxTrackerStartDate => DateTime.Today;

    public async Task LoadAsync()
    {
        await LoadAsync(SelectedTracker?.Id);
    }

    [RelayCommand]
    private void ShowAddTrackerForm()
    {
        IsEditingTracker = false;
        _editingTrackerId = Guid.Empty;
        NewTrackerName = string.Empty;
        NewTrackerStartDate = DateTime.Today;
        NewTrackerCost = string.Empty;
        NewTrackerUnits = string.Empty;
        ValidationMessage = string.Empty;
        IsAddTrackerFormVisible = true;
    }

    [RelayCommand]
    private void EditTracker(HarmfulHabitTrackerItemViewModel? tracker)
    {
        var target = tracker ?? SelectedTracker;
        if (target == null) return;

        IsEditingTracker = true;
        _editingTrackerId = target.Id;
        NewTrackerName = target.HarmfulHabitName;
        NewTrackerStartDate = target.StartDate;
        NewTrackerCost = target.CostPerUnit > 0 ? target.CostPerUnit.ToString(CultureInfo.CurrentCulture) : string.Empty;
        NewTrackerUnits = target.UnitsPerDay > 0 ? target.UnitsPerDay.ToString(CultureInfo.CurrentCulture) : string.Empty;
        ValidationMessage = string.Empty;
        IsAddTrackerFormVisible = true;
    }

    [RelayCommand]
    private void HideAddTrackerForm()
    {
        IsAddTrackerFormVisible = false;
        IsEditingTracker = false;
        _editingTrackerId = Guid.Empty;
        ValidationMessage = string.Empty;
        NewTrackerName = string.Empty;
        NewTrackerStartDate = DateTime.Today;
        NewTrackerCost = string.Empty;
        NewTrackerUnits = string.Empty;
    }

    [RelayCommand]
    private async Task AddTrackerAsync()
    {
        ValidationMessage = string.Empty;

        var normalizedName = (NewTrackerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            ValidationMessage = AppResources.HabitTrackerNameRequiredMessage;
            return;
        }

        if (Trackers.Any(x => x.Id != _editingTrackerId
                              && string.Equals(x.HarmfulHabitName, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = AppResources.HabitTrackerDuplicateMessage;
            return;
        }

        decimal cost = decimal.TryParse(NewTrackerCost, NumberStyles.Number, CultureInfo.CurrentCulture, out var c) && c > 0 ? c : 0m;
        double units = double.TryParse(NewTrackerUnits, NumberStyles.Number, CultureInfo.CurrentCulture, out var u) && u > 0 ? u : 0;

        HarmfulHabitTracker tracker;
        if (IsEditingTracker)
        {
            // Load the full tracker so marked days and relapses are preserved.
            tracker = await _habitService.GetHarmfulHabitTrackerByIdAsync(_editingTrackerId)
                      ?? new HarmfulHabitTracker { Id = _editingTrackerId };
        }
        else
        {
            tracker = new HarmfulHabitTracker();
        }

        tracker.HarmfulHabitName = normalizedName;
        tracker.StartDate = NewTrackerStartDate.Date;
        tracker.CostPerUnit = cost;
        tracker.UnitsPerDay = units;

        await _habitService.SaveHarmfulHabitTrackerAsync(tracker);

        var savedId = tracker.Id;
        HideAddTrackerForm();
        await LoadAsync(savedId);
    }

    [RelayCommand]
    private async Task DeleteTrackerAsync(HarmfulHabitTrackerItemViewModel? tracker)
    {
        if (tracker == null) return;

        var result = await _dialogService.ShowConfirmationAsync(
            "Delete",
            "Are you sure you want to delete this?");
            
        if (!result) return;

        await _habitService.DeleteHarmfulHabitTrackerAsync(tracker.Id);
        
        Trackers.Remove(tracker);
        if (SelectedTracker?.Id == tracker.Id)
        {
            SelectedTracker = Trackers.FirstOrDefault();
        }
        
        OnPropertyChanged(nameof(HasTrackers));
        OnPropertyChanged(nameof(HasNoTrackers));
    }

    [RelayCommand]
    private void SelectTracker(HarmfulHabitTrackerItemViewModel? tracker)
    {
        if (tracker != null)
        {
            SelectedTracker = tracker;
        }
    }

    [RelayCommand]
    private async Task ToggleDayAsync(HarmfulHabitDayViewModel? day)
    {
        if (day == null || SelectedTracker == null || day.IsFuture)
        {
            return;
        }

        var isMarked = !day.IsMarked;
        await _habitService.SetHarmfulHabitDayMarkedAsync(SelectedTracker.Id, day.Date, isMarked);

        day.IsMarked = isMarked;
        if (isMarked)
        {
            SelectedTracker.MarkDay(day.Date);
            
            // Якщо відмітили день, який є останнім у рядку (кратний 5), та він є останнім у списку - додаємо ще 5 днів
            if (day.DayNumber % 5 == 0 && day.Date == TrackerDays.Last().Date)
            {
                var currentDate = day.Date.AddDays(1);
                var dayNumber = day.DayNumber + 1;
                for (int i = 0; i < 5; i++)
                {
                    TrackerDays.Add(new HarmfulHabitDayViewModel(dayNumber, currentDate, false));
                    currentDate = currentDate.AddDays(1);
                    dayNumber++;
                }
            }
        }
        else
        {
            SelectedTracker.UnmarkDay(day.Date);
        }
    }

    [RelayCommand]
    private async Task RelapseAsync(HarmfulHabitTrackerItemViewModel? tracker)
    {
        var target = tracker ?? SelectedTracker;
        if (target == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            AppResources.QuitRelapseConfirmTitle,
            AppResources.QuitRelapseConfirmMessage,
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);

        if (!confirm) return;

        var today = DateTime.Today;
        await _habitService.AddRelapseAsync(target.Id, today, null);
        target.AddRelapse(today);
    }

    /// <summary>Ticks the live clean-time counters; called from the page's dispatcher timer.</summary>
    public void RefreshLiveStats()
    {
        foreach (var tracker in Trackers)
        {
            tracker.RefreshLive();
        }
    }

    partial void OnSelectedTrackerChanged(HarmfulHabitTrackerItemViewModel? value)
    {
        foreach (var tracker in Trackers)
        {
            tracker.IsSelected = ReferenceEquals(tracker, value);
        }

        RebuildTrackerDays(value);
    }

    private async Task LoadAsync(Guid? selectedTrackerId)
    {
        IsBusy = true;

        try
        {
            var trackers = await _habitService.GetHarmfulHabitTrackersAsync();
            var orderedTrackers = trackers
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.StartDate)
                .Select(x => new HarmfulHabitTrackerItemViewModel(x))
                .ToList();

            Trackers.Clear();
            foreach (var tracker in orderedTrackers)
            {
                Trackers.Add(tracker);
            }

            OnPropertyChanged(nameof(HasTrackers));
            OnPropertyChanged(nameof(HasNoTrackers));

            SelectedTracker = Trackers.FirstOrDefault(x => x.Id == selectedTrackerId) ?? Trackers.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildTrackerDays(HarmfulHabitTrackerItemViewModel? tracker)
    {
        TrackerDays.Clear();

        if (tracker == null)
        {
            return;
        }

        var currentDate = tracker.StartDate.Date;
        var dayNumber = 1;

        int maxMarkedDayNum = 0;
        foreach (var markedDate in tracker.MarkedDays)
        {
            int d = (markedDate.Date - tracker.StartDate.Date).Days + 1;
            if (d > maxMarkedDayNum) maxMarkedDayNum = d;
        }

        // Гарантуємо щонайменше 30 днів (щоб кружки заповнювали екран) 
        // і щоб кількість днів завжди була кратна 5 (повні рядки)
        int requiredDays = Math.Max(30, ((maxMarkedDayNum + 4) / 5) * 5 + 5);

        for (int i = 0; i < requiredDays; i++)
        {
            TrackerDays.Add(new HarmfulHabitDayViewModel(dayNumber, currentDate, tracker.MarkedDays.Contains(currentDate)));
            currentDate = currentDate.AddDays(1);
            dayNumber++;
        }
    }
}

public partial class HarmfulHabitTrackerItemViewModel : ObservableObject
{
    public HarmfulHabitTrackerItemViewModel(HarmfulHabitTracker tracker)
    {
        Id = tracker.Id;
        HarmfulHabitName = tracker.HarmfulHabitName;
        StartDate = tracker.StartDate.Date;
        MarkedDays = tracker.MarkedDays.Select(x => x.Date).ToHashSet();
        markedDaysCount = MarkedDays.Count;

        CostPerUnit = tracker.CostPerUnit;
        UnitsPerDay = tracker.UnitsPerDay;
        Relapses = (tracker.Relapses ?? new List<RelapseEvent>()).OrderByDescending(r => r.Date).ToList();
        RefreshLive();
    }

    public Guid Id { get; }
    public string HarmfulHabitName { get; }
    public DateTime StartDate { get; }
    public HashSet<DateTime> MarkedDays { get; }
    public string StartDateText => StartDate.ToString("d", CultureInfo.CurrentCulture);

    public decimal CostPerUnit { get; private set; }
    public double UnitsPerDay { get; private set; }
    public List<RelapseEvent> Relapses { get; private set; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkedDaysCountText))]
    private int markedDaysCount;

    public string MarkedDaysCountText => MarkedDaysCount.ToString(CultureInfo.CurrentCulture);

    // --- Quit-tracker live stats ---

    [ObservableProperty]
    private string _liveTimeText = string.Empty;

    [ObservableProperty]
    private string _cleanDaysText = "0";

    [ObservableProperty]
    private string _moneySavedText = string.Empty;

    [ObservableProperty]
    private bool _hasMoney;

    [ObservableProperty]
    private string _nextMilestoneText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelapses))]
    private string _relapseCountText = "0";

    public bool HasRelapses => Relapses.Count > 0;

    private HarmfulHabitTracker BuildSnapshot() => new()
    {
        Id = Id,
        StartDate = StartDate,
        CostPerUnit = CostPerUnit,
        UnitsPerDay = UnitsPerDay,
        Relapses = Relapses
    };

    /// <summary>Recomputes the live clean-time / money / milestone texts from the current moment.</summary>
    public void RefreshLive()
    {
        var now = DateTime.Now;
        var snapshot = BuildSnapshot();

        var cleanSince = QuitTrackerCalculator.CleanSince(snapshot, now.Date);
        int cleanDays = QuitTrackerCalculator.CleanDays(snapshot, now.Date);

        var elapsed = now - cleanSince;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        LiveTimeText = string.Format(
            AppResources.QuitLiveTimeFormat,
            (int)elapsed.TotalDays, elapsed.Hours, elapsed.Minutes, elapsed.Seconds);
        CleanDaysText = cleanDays.ToString(CultureInfo.CurrentCulture);

        var money = QuitTrackerCalculator.MoneySaved(snapshot, now.Date);
        HasMoney = money > 0m;
        MoneySavedText = money.ToString("N2", CultureInfo.CurrentCulture);

        var next = QuitTrackerCalculator.NextMilestone(cleanDays);
        NextMilestoneText = next.HasValue
            ? string.Format(AppResources.QuitNextMilestoneFormat, next.Value)
            : AppResources.QuitAllMilestones;

        RelapseCountText = Relapses.Count.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Records a relapse locally (the service persists it) and refreshes the live stats.</summary>
    public void AddRelapse(DateTime date)
    {
        Relapses = Relapses.Append(new RelapseEvent { Date = date.Date }).OrderByDescending(r => r.Date).ToList();
        OnPropertyChanged(nameof(Relapses));
        OnPropertyChanged(nameof(HasRelapses));
        RefreshLive();
    }

    public void MarkDay(DateTime date)
    {
        if (MarkedDays.Add(date.Date))
        {
            MarkedDaysCount++;
        }
    }

    public void UnmarkDay(DateTime date)
    {
        if (MarkedDays.Remove(date.Date))
        {
            MarkedDaysCount--;
        }
    }
}

public partial class HarmfulHabitDayViewModel : ObservableObject
{
    public HarmfulHabitDayViewModel(int dayNumber, DateTime date, bool isMarked)
    {
        DayNumber = dayNumber;
        Date = date.Date;
        this.isMarked = isMarked;
    }

    public int DayNumber { get; }
    public DateTime Date { get; }
    public string DayNumberText => DayNumber.ToString(CultureInfo.CurrentCulture);
    public string DateText => Date.ToString("dd.MM", CultureInfo.CurrentCulture);
    public bool IsFuture => Date.Date > DateTime.Today;

    [ObservableProperty]
    private bool isMarked;
}