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

    public CalendarSectionViewModel CalendarSection { get; }
    public PlannerSectionViewModel PlannerSection { get; }
    public QuickMenuViewModel QuickMenuSection { get; }
    public HabitsSectionViewModel HabitsSection { get; }
    public CycleStatusViewModel CycleStatusSection { get; }

    [ObservableProperty]
    private DiaryEntryViewModel? _currentEntry;

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
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnEntryPropertyChanged;
            newValue.Habits.CollectionChanged += OnHabitsCollectionChanged;
            foreach (var h in newValue.Habits)
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
        ScheduleAutoSave();
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsBusy || CurrentEntry == null || e.PropertyName == "IsBusy") return;
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveDebouncer.Debounce(async () =>
        {
            if (CurrentEntry != null)
            {
                CurrentEntry.SyncToModel();
                await _diaryService.SaveEntryAsync(CurrentEntry.Model);
                System.Diagnostics.Debug.WriteLine($"Auto-saved entry for {CurrentEntry.Date:dd.MM.yyyy}");
            }
        });
    }

    public Task FlushAutoSaveAsync()
    {
        return _autoSaveDebouncer.FlushAsync(async () =>
        {
            if (CurrentEntry != null)
            {
                CurrentEntry.SyncToModel();
                await _diaryService.SaveEntryAsync(CurrentEntry.Model);
            }
        });
    }

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");
        await LoadEntriesForDateAsync(date);
        await CycleStatusSection.UpdateForDateAsync(date);

        if (CurrentEntry != null && CycleStatusSection.IsVisible) {
            CurrentEntry.CycleDay = CycleStatusSection.CycleDay;
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
        IsBusy = true;
        var entry = await _diaryService.GetEntryForDateAsync(date.Date);
        await _diaryHabitSyncService.SyncHabitsForEntryAsync(entry);
        CurrentEntry = new DiaryEntryViewModel(entry);
        IsBusy = false;
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
            IsBusy = false;
        }
    }

    private async Task UpdateStreakAsync()
    {
        CurrentStreak = await _diaryService.GetCurrentStreakAsync();
        OnPropertyChanged(nameof(IsStreakVisible));
    }

    [RelayCommand]
    public void OpenMenu()
    {
        Microsoft.Maui.Controls.Shell.Current.FlyoutIsPresented = true;
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
