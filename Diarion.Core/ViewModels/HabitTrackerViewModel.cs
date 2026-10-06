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
    private bool _newTrackerReminderEnabled;

    [ObservableProperty]
    private TimeSpan _newTrackerReminderTime = new(9, 0, 0);

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
    private readonly INotificationService _notificationService;
    private readonly IProfileService _profileService;

    public HabitTrackerViewModel(
        IHabitService habitService,
        IDialogService dialogService,
        INotificationService notificationService,
        IProfileService profileService)
    {
        _habitService = habitService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _profileService = profileService;
        Title = AppResources.HabitTrackerTitle;
    }

    /// <summary>Money saved is money, so it wears the same currency as the finance screens.</summary>
    private string _currencyCode = MoneyFormatter.FallbackCode;

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
        NewTrackerReminderEnabled = false;
        NewTrackerReminderTime = new TimeSpan(9, 0, 0);
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
        NewTrackerReminderEnabled = target.ReminderTime.HasValue;
        NewTrackerReminderTime = target.ReminderTime ?? new TimeSpan(9, 0, 0);
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
        NewTrackerReminderEnabled = false;
        NewTrackerReminderTime = new TimeSpan(9, 0, 0);
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

        var reminder = NewTrackerReminderEnabled ? NewTrackerReminderTime : (TimeSpan?)null;

        tracker.HarmfulHabitName = normalizedName;
        tracker.StartDate = NewTrackerStartDate.Date;
        tracker.CostPerUnit = cost;
        tracker.UnitsPerDay = units;
        tracker.ReminderTime = reminder;

        await _habitService.SaveHarmfulHabitTrackerAsync(tracker);
        await ApplyReminderAsync(tracker.Id, normalizedName, reminder);

        var savedId = tracker.Id;
        HideAddTrackerForm();
        await LoadAsync(savedId);
    }

    private async Task ApplyReminderAsync(Guid trackerId, string name, TimeSpan? reminder)
    {
        if (reminder.HasValue)
        {
            await _notificationService.RequestPermissionsAsync();
            _notificationService.ScheduleHabitReminder(trackerId, name, reminder.Value, null);
        }
        else
        {
            _notificationService.CancelHabitReminder(trackerId);
        }
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
        _notificationService.CancelHabitReminder(tracker.Id);

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
        }
        else
        {
            SelectedTracker.UnmarkDay(day.Date);
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
            _currencyCode = (await _profileService.GetUserProfileAsync())?.GetEffectiveCurrencyCode()
                            ?? MoneyFormatter.FallbackCode;

            var trackers = await _habitService.GetHarmfulHabitTrackersAsync();
            var orderedTrackers = trackers
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.StartDate)
                .Select(x => new HarmfulHabitTrackerItemViewModel(x, _currencyCode))
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

        var startDate = tracker.StartDate.Date;
        int elapsedDays = Math.Max(1, (DateTime.Today - startDate).Days + 1);

        // Кожен день від старту до сьогодні має бути доступний для відмітки, далі —
        // заморожений хвіст. Щонайменше 30 днів і завжди кратно 5 (повні рядки).
        int requiredDays = Math.Max(30, ((elapsedDays + 4) / 5) * 5 + 5);

        var currentDate = startDate;
        for (int dayNumber = 1; dayNumber <= requiredDays; dayNumber++)
        {
            TrackerDays.Add(new HarmfulHabitDayViewModel(dayNumber, currentDate, tracker.MarkedDays.Contains(currentDate)));
            currentDate = currentDate.AddDays(1);
        }
    }
}

public partial class HarmfulHabitTrackerItemViewModel : ObservableObject
{
    /// <summary>The currency the saved amount is shown in, handed down from the page's profile read.</summary>
    private readonly string _currencyCode;

    public HarmfulHabitTrackerItemViewModel(HarmfulHabitTracker tracker, string currencyCode)
    {
        _currencyCode = currencyCode;
        Id = tracker.Id;
        HarmfulHabitName = tracker.HarmfulHabitName;
        StartDate = tracker.StartDate.Date;
        MarkedDays = tracker.MarkedDays.Select(x => x.Date).ToHashSet();
        markedDaysCount = MarkedDays.Count;

        CostPerUnit = tracker.CostPerUnit;
        UnitsPerDay = tracker.UnitsPerDay;
        ReminderTime = tracker.ReminderTime;
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
    public TimeSpan? ReminderTime { get; private set; }

    /// <summary>Kept so the money-saved maths still resets on the latest relapse; no longer shown.</summary>
    public List<RelapseEvent> Relapses { get; private set; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkedDaysCountText))]
    private int markedDaysCount;

    public string MarkedDaysCountText => MarkedDaysCount.ToString(CultureInfo.CurrentCulture);

    [ObservableProperty]
    private string _moneySavedText = string.Empty;

    [ObservableProperty]
    private bool _hasMoney;

    private HarmfulHabitTracker BuildSnapshot() => new()
    {
        Id = Id,
        StartDate = StartDate,
        CostPerUnit = CostPerUnit,
        UnitsPerDay = UnitsPerDay,
        Relapses = Relapses
    };

    /// <summary>Recomputes the money-saved estimate. Day-granular, so once per load is enough.</summary>
    public void RefreshLive()
    {
        var money = QuitTrackerCalculator.MoneySaved(BuildSnapshot(), DateTime.Today);
        HasMoney = money > 0m;
        // Formatted like every other amount in the app: a bare number here while the finance
        // screens carry a symbol reads as a different unit rather than as restraint.
        MoneySavedText = MoneyFormatter.Format(money, _currencyCode);
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