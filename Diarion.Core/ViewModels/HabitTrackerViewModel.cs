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
    [NotifyPropertyChangedFor(nameof(HasSelectedTracker))]
    private HarmfulHabitTrackerItemViewModel? _selectedTracker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isAddTrackerFormVisible;

    public HabitTrackerViewModel(IHabitService habitService)
    {
        _habitService = habitService;
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
        IsAddTrackerFormVisible = true;
    }

    [RelayCommand]
    private void HideAddTrackerForm()
    {
        IsAddTrackerFormVisible = false;
        ValidationMessage = string.Empty;
        NewTrackerName = string.Empty;
        NewTrackerStartDate = DateTime.Today;
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

        if (Trackers.Any(x => string.Equals(x.HarmfulHabitName, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = AppResources.HabitTrackerDuplicateMessage;
            return;
        }

        var tracker = new HarmfulHabitTracker
        {
            HarmfulHabitName = normalizedName,
            StartDate = NewTrackerStartDate.Date
        };

        await _habitService.SaveHarmfulHabitTrackerAsync(tracker);

        HideAddTrackerForm();
        await LoadAsync(tracker.Id);
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
    }

    public Guid Id { get; }
    public string HarmfulHabitName { get; }
    public DateTime StartDate { get; }
    public HashSet<DateTime> MarkedDays { get; }
    public string StartDateText => StartDate.ToString("d", CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkedDaysCountText))]
    private int markedDaysCount;

    public string MarkedDaysCountText => MarkedDaysCount.ToString(CultureInfo.CurrentCulture);

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