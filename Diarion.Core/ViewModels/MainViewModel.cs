using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Diagnostics;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services;
using Diarion.Core.Services;
using Diarion.Helpers;

namespace Diarion.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private readonly IDiaryHabitSyncService _diaryHabitSyncService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IHealthDataService _healthDataService;
    private readonly IProfileService _profileService;

    public CalendarSectionViewModel CalendarSection { get; }
    public PlannerSectionViewModel PlannerSection { get; }
    public QuickMenuViewModel QuickMenuSection { get; }
    public HabitsSectionViewModel HabitsSection { get; }
    public CycleStatusViewModel CycleStatusSection { get; }

    [ObservableProperty]
    private DiaryEntryViewModel? _currentEntry;

    // Drives which home-screen blocks are shown; the sections bind their IsVisible to these flags.
    [ObservableProperty]
    private UserProfile _profile = new();

    // False until the first LoadEntriesAsync completes. Home content stays hidden behind the
    // loading indicator until then, so the user never sees the default all-blocks-visible layout
    // (Profile starts as new() with every block flag true) before the real profile is applied.
    [ObservableProperty]
    private bool _isContentReady;

    public bool IsLoading => IsBusy || !IsContentReady;

    partial void OnIsContentReadyChanged(bool value) => OnPropertyChanged(nameof(IsLoading));
    protected override void OnBusyStateChanged() => OnPropertyChanged(nameof(IsLoading));

    [ObservableProperty]
    private int _currentStreak;

    public bool IsStreakVisible => CurrentStreak > 0;

    [ObservableProperty]
    private bool _isPlannerMode;

    [ObservableProperty]
    private bool _isDiaryMode = true;

    private readonly AsyncDebouncer _autoSaveDebouncer = new AsyncDebouncer(TimeSpan.FromSeconds(1));

    public MainViewModel(
        IDiaryService diaryService, 
        IDiaryHabitSyncService diaryHabitSyncService,
        INavigationService navigationService,
        IDialogService dialogService,
        IHealthDataService healthDataService,
        IProfileService profileService,
        CalendarSectionViewModel calendarSection,
        PlannerSectionViewModel plannerSection,
        QuickMenuViewModel quickMenuSection,
        HabitsSectionViewModel habitsSection,
        CycleStatusViewModel cycleStatusSection)
    {
        using var trace = StartupTrace.Measure("MainViewModel..ctor");
        _diaryService = diaryService;
        _diaryHabitSyncService = diaryHabitSyncService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _healthDataService = healthDataService;
        _profileService = profileService;
        
        CalendarSection = calendarSection;
        PlannerSection = plannerSection;
        QuickMenuSection = quickMenuSection;
        HabitsSection = habitsSection;
        CycleStatusSection = cycleStatusSection;

        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;

        WeakReferenceMessenger.Default.Register<DateSelectedMessage>(this, (r, m) =>
        {
            _ = LoadDayContentAsync(m.SelectedDate);
        });

        WeakReferenceMessenger.Default.Register<TodoChangedMessage>(this, (r, m) =>
        {
            _ = CalendarSection.UpdateCalendarTasksForDayAsync(m.Date);
        });

        CalendarSection.Initialize();
        QuickMenuSection.Initialize();
    }

    [RelayCommand]
    public async Task SwitchToPlannerModeAsync()
    {
        if (IsPlannerMode) return;

        IsPlannerMode = true;
        IsDiaryMode = false;
        await PlannerSection.LoadTodosForDateAsync(CalendarSection.GetSelectedDate());
    }

    [RelayCommand]
    public void SwitchToDiaryMode()
    {
        IsPlannerMode = false;
        IsDiaryMode = true;
        PlannerSection.ClearTodos();
    }

    partial void OnCurrentEntryChanged(DiaryEntryViewModel? oldValue, DiaryEntryViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnEntryPropertyChanged;
            oldValue.Habits.CollectionChanged -= OnHabitsCollectionChanged;
            foreach (var h in oldValue.Habits)
            {
                h.PropertyChanged -= OnEntryPropertyChanged;
            }
            // Fixed 17 slots, never added or removed, so no CollectionChanged handling is needed.
            foreach (var h in oldValue.HourlyMood)
            {
                h.PropertyChanged -= OnEntryPropertyChanged;
            }
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnEntryPropertyChanged;
            newValue.Habits.CollectionChanged += OnHabitsCollectionChanged;
            foreach (var h in newValue.Habits)
            {
                h.PropertyChanged += OnEntryPropertyChanged;
            }
            foreach (var h in newValue.HourlyMood)
            {
                h.PropertyChanged += OnEntryPropertyChanged;
            }
        }

        HabitsSection.Entry = newValue;
    }

    private void OnHabitsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (HabitItemViewModel item in e.OldItems) item.PropertyChanged -= OnEntryPropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (HabitItemViewModel item in e.NewItems) item.PropertyChanged += OnEntryPropertyChanged;
        }

        // Subscriptions above must always be rebuilt; only the save is skipped while loading, matching
        // OnEntryPropertyChanged. Repopulating the list on load is not a user edit.
        if (IsBusy) return;
        ScheduleAutoSave();
    }

    /// <summary>
    /// Presentation-only state that lives on the entry view-models but is not part of the entry.
    /// Saving on these would persist a blank row just because the user expanded a panel or highlighted
    /// an hour — the same way browsing a day used to create entries.
    /// </summary>
    private static readonly HashSet<string> NonPersistedProperties = new()
    {
        nameof(IsBusy),
        nameof(DiaryEntryViewModel.SelectedHour),
        nameof(DiaryEntryViewModel.IsHourSelected),
        nameof(DiaryEntryViewModel.IsHourlyExpanded),
        nameof(DiaryEntryViewModel.CurrentMood),
        nameof(HourMoodViewModel.IsSelected),
    };

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsBusy || CurrentEntry == null) return;
        if (e.PropertyName != null && NonPersistedProperties.Contains(e.PropertyName)) return;
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        // Capture the entry being edited NOW; the debounced save must persist THIS entry even if
        // the user switches to another day before the debounce fires (otherwise edits are lost).
        var entry = CurrentEntry;
        if (entry == null)
        {
            return;
        }

        _autoSaveDebouncer.Debounce(async () =>
        {
            entry.SyncToModel();
            await _diaryService.SaveEntryAsync(entry.Model);
            System.Diagnostics.Debug.WriteLine($"Auto-saved entry for {entry.Date:dd.MM.yyyy}");
        });
    }

    public Task FlushAutoSaveAsync()
    {
        // The pending action already captured the correct entry (see ScheduleAutoSave).
        return _autoSaveDebouncer.FlushAsync();
    }

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");

        // Persist any pending edits for the currently-open day BEFORE switching, otherwise the
        // debounced save would still be pending and the previous day's edits could be lost.
        await FlushAutoSaveAsync();

        // Own the busy scope for the whole load. CycleDay below is written by us, not by the user, so
        // autosave must stay suppressed until it is set — otherwise merely browsing a day persists an
        // otherwise-empty entry, which then counts as a journaled day in the streak.
        var wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            await LoadEntriesForDateAsync(date);
            await CycleStatusSection.UpdateForDateAsync(date);

            if (CurrentEntry != null && CycleStatusSection.IsVisible)
            {
                CurrentEntry.CycleDay = CycleStatusSection.CycleDay;
            }
        }
        finally
        {
            IsBusy = wasBusy;
        }

        if (IsPlannerMode)
        {
            await PlannerSection.LoadTodosForDateAsync(date);
            return;
        }

        PlannerSection.ClearTodos();
    }

    private async Task LoadEntriesForDateAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadEntriesForDateAsync");
        var entry = await _diaryService.GetEntryForDateAsync(date.Date);
        await _diaryHabitSyncService.SyncHabitsForEntryAsync(entry);
        CurrentEntry = new DiaryEntryViewModel(entry);
    }

    [RelayCommand]
    public async Task ImportSleepDataAsync()
    {
        if (CurrentEntry == null) return;

        if (!await _healthDataService.IsSupportedAsync())
        {
            await _dialogService.ShowAlertAsync("Error", "Health sync is not supported on this device.", "OK");
            return;
        }

        var hasPermission = await _healthDataService.RequestPermissionsAsync();
        if (hasPermission)
        {
            var data = await _healthDataService.GetSleepDataAsync(CurrentEntry.Date);
            if (data.SleepStart.HasValue)
                CurrentEntry.SleepStart = data.SleepStart;
            if (data.SleepEnd.HasValue)
                CurrentEntry.SleepEnd = data.SleepEnd;
        }
    }

    [RelayCommand]
    public async Task SaveEntryAsync()
    {
        if (CurrentEntry == null) return;
        
        IsBusy = true;
        try
        {
            CurrentEntry.SyncToModel();
            await _diaryService.SaveEntryAsync(CurrentEntry.Model);
            await UpdateStreakAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving entry: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoadEntriesAsync()
    {
        if (IsBusy)
            return;

        using var _ = StartupTrace.Measure("MainViewModel.LoadEntriesAsync");

        try
        {
            IsBusy = true;
            // Reload so home-screen block visibility reflects changes made in settings.
            Profile = await _profileService.GetUserProfileAsync();
            await LoadDayContentAsync(CalendarSection.GetSelectedDate());
            await CalendarSection.UpdateCalendarTasksCompletionAsync();
            await UpdateStreakAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading entries: {ex.Message}");
        }
        finally
        {
            IsContentReady = true;
            IsBusy = false;
        }
    }

    private async Task UpdateStreakAsync()
    {
        CurrentStreak = (await _diaryService.GetCurrentStreakAsync()).Length;
        OnPropertyChanged(nameof(IsStreakVisible));
    }

    [RelayCommand]
    public void OpenMenu()
    {
        _ = _navigationService.OpenFlyoutAsync();
    }

    [RelayCommand]
    public async Task GoToNewEntryAsync()
    {
        await _navigationService.NavigateToAsync("DiaryDetail");
    }

    [RelayCommand]
    public async Task GoToNewTodoAsync()
    {
        var selectedDate = Uri.EscapeDataString(CalendarSection.GetSelectedDate().ToString("O", CultureInfo.InvariantCulture));
        await _navigationService.NavigateToAsync($"TodoDetail?Date={selectedDate}");
    }

    [RelayCommand]
    public async Task OpenCreateItemAsync()
    {
        if (IsPlannerMode)
        {
            await GoToNewTodoAsync();
            return;
        }

        await GoToNewEntryAsync();
    }

    [RelayCommand]
    public async Task GoToEntryDetailsAsync(DiaryEntryViewModel entry)
    {
        if (entry == null) return;
        await _navigationService.NavigateToAsync($"DiaryDetail?Id={entry.Id}");
    }
}
