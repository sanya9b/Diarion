using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Diagnostics;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private readonly IHabitService _habitService;
    private readonly IMenstrualCycleService _menstrualCycleService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    public CalendarSectionViewModel CalendarSection { get; }
    public PlannerSectionViewModel PlannerSection { get; }
    public QuickMenuViewModel QuickMenuSection { get; }

    [ObservableProperty]
    private DiaryEntryViewModel? _currentEntry;

    [ObservableProperty]
    private bool _isEditHabitsMode;

    [ObservableProperty]
    private string _cycleDayText = string.Empty;

    [ObservableProperty]
    private string _pregnancyProbabilityText = string.Empty;

    [ObservableProperty]
    private bool _isCycleInfoVisible;

    [ObservableProperty]
    private Microsoft.Maui.Graphics.Color _pregnancyProbabilityColor = Microsoft.Maui.Graphics.Colors.Transparent;

    [ObservableProperty]
    private bool _isPlannerMode;

    [ObservableProperty]
    private bool _isDiaryMode = true;

    private CancellationTokenSource? _autoSaveCts;

    public MainViewModel(
        IDiaryService diaryService, 
        IHabitService habitService, 
        IMenstrualCycleService menstrualCycleService, 
        IProfileService profileService,
        INavigationService navigationService,
        IDialogService dialogService,
        CalendarSectionViewModel calendarSection,
        PlannerSectionViewModel plannerSection,
        QuickMenuViewModel quickMenuSection)
    {
        using var trace = StartupTrace.Measure("MainViewModel..ctor");
        _diaryService = diaryService;
        _habitService = habitService;
        _menstrualCycleService = menstrualCycleService;
        _profileService = profileService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        
        CalendarSection = calendarSection;
        PlannerSection = plannerSection;
        QuickMenuSection = quickMenuSection;

        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;

        CalendarSection.OnDateSelected += OnDateSelected;
        PlannerSection.OnTodoChanged += OnTodoChanged;

        CalendarSection.Initialize();
        QuickMenuSection.Initialize();
    }

    private void OnDateSelected(DateTime date)
    {
        _ = LoadDayContentAsync(date);
    }

    private void OnTodoChanged(DateTime date)
    {
        _ = CalendarSection.UpdateCalendarTasksForDayAsync(date);
    }

    [RelayCommand]
    public void ToggleEditHabitsMode()
    {
        IsEditHabitsMode = !IsEditHabitsMode;
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
    }

    private void OnHabitsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (HabitItem item in e.OldItems) item.PropertyChanged -= OnEntryPropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (HabitItem item in e.NewItems) item.PropertyChanged += OnEntryPropertyChanged;
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
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (!token.IsCancellationRequested && CurrentEntry != null)
                {
                    CurrentEntry.SyncToModel();
                    await _diaryService.SaveEntryAsync(CurrentEntry.Model);
                    System.Diagnostics.Debug.WriteLine($"Auto-saved entry for {CurrentEntry.Date:dd.MM.yyyy}");
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    public async Task FlushAutoSaveAsync()
    {
        if (_autoSaveCts != null && !_autoSaveCts.IsCancellationRequested)
        {
            _autoSaveCts.Cancel();
            if (CurrentEntry != null)
            {
                CurrentEntry.SyncToModel();
                await _diaryService.SaveEntryAsync(CurrentEntry.Model);
            }
        }
    }

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");
        await LoadEntriesForDateAsync(date);

        var profile = await _profileService.GetUserProfileAsync();
        var cycleInfo = _menstrualCycleService.GetCycleInfoForDate(date, profile);

        if (cycleInfo.IsTrackingEnabled)
        {
            IsCycleInfoVisible = true;
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
            
            if (CurrentEntry != null) {
                CurrentEntry.CycleDay = cycleInfo.DayOfCycle.ToString();
            }
        }
        else
        {
            IsCycleInfoVisible = false;
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
        CurrentEntry = new DiaryEntryViewModel(entry);
        IsBusy = false;
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка збереження запису: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка завантаження записів: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
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

    // HABITS SECTION
    private HabitItem? _draggedHabit;

    [RelayCommand]
    public void DragHabitStarting(HabitItem item)
    {
        _draggedHabit = item;
    }

    [RelayCommand]
    public void DropHabitCompleted()
    {
        _draggedHabit = null;
    }

    [RelayCommand]
    public async Task ReorderHabitsAsync(HabitItem targetItem)
    {
        if (_draggedHabit == null || targetItem == null || _draggedHabit == targetItem)
            return;

        if (CurrentEntry == null) return;

        int oldIndex = CurrentEntry.Habits.IndexOf(_draggedHabit);
        int newIndex = CurrentEntry.Habits.IndexOf(targetItem);

        if (oldIndex < 0 || newIndex < 0)
            return;

        CurrentEntry.Habits.Move(oldIndex, newIndex);

        var orderedIds = CurrentEntry.Habits.Select(h => h.HabitId).ToList();
        await _habitService.UpdateHabitDefinitionsOrderAsync(orderedIds);
    }

    [RelayCommand]
    public async Task AddHabitAsync()
    {
        string result = await _dialogService.ShowPromptAsync(
            Diarion.Resources.Localization.AppResources.AddHabitPromptTitle,
            Diarion.Resources.Localization.AppResources.AddHabitPromptMessage);
        if (!string.IsNullOrWhiteSpace(result))
        {
            var def = new HabitDefinition { Name = result.Trim(), CreatedAt = CalendarSection.GetSelectedDate() };
            await _habitService.AddHabitDefinitionAsync(def);
            
            if (CurrentEntry != null)
            {
                CurrentEntry.Habits.Add(new HabitItem { HabitId = def.Id, Name = def.Name });
            }
        }
    }

    [RelayCommand]
    public async Task DeleteHabitAsync(HabitItem item)
    {
        if (item == null || CurrentEntry == null) return;
        
        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteHabitConfirmTitle,
            string.Format(Diarion.Resources.Localization.AppResources.DeleteHabitConfirmMessage, item.Name),
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
        if (!confirm) return;

        await _habitService.DeleteHabitDefinitionAsync(item.HabitId, CalendarSection.GetSelectedDate());
        CurrentEntry.Habits.Remove(item);
    }
}
