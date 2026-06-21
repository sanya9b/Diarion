using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Diagnostics;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private readonly IProfileService _profileService;
    private readonly ITodoService _todoService;
    private readonly IHabitService _habitService;

    [ObservableProperty]
    private DiaryEntry? _currentEntry;

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

    private CancellationTokenSource? _autoSaveCts;

    [RelayCommand]
    public void ToggleEditHabitsMode()
    {
        IsEditHabitsMode = !IsEditHabitsMode;
    }

    partial void OnCurrentEntryChanged(DiaryEntry? oldValue, DiaryEntry? newValue)
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
        // Ігноруємо службові події або якщо запис ще завантажується
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
                await Task.Delay(1000, token); // Debounce на 1 секунду
                if (!token.IsCancellationRequested && CurrentEntry != null)
                {
                    await _diaryService.SaveEntryAsync(CurrentEntry);
                    System.Diagnostics.Debug.WriteLine($"Auto-saved entry for {CurrentEntry.Date:dd.MM.yyyy}");
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    [ObservableProperty]
    private bool _isCalendarExpanded = false;

    [ObservableProperty]
    private List<CalendarDay> _calendarDays = new();

    public System.Collections.ObjectModel.ObservableCollection<TodoItem> Todos { get; } = new();

    [ObservableProperty]
    private string _currentMonthName = string.Empty;

    [ObservableProperty]
    private string _currentYear = string.Empty;

    [ObservableProperty]
    private string _selectedDateDayName = string.Empty;

    [ObservableProperty]
    private string _todayMonthShort = string.Empty;

    [ObservableProperty]
    private string _todayDayNumber = string.Empty;

    [ObservableProperty]
    private double _todayTaskCompletionPercentage;

    [ObservableProperty]
    private Microsoft.Maui.Graphics.Color _todayProgressColor = Microsoft.Maui.Graphics.Colors.Transparent;

    public System.Collections.ObjectModel.ObservableCollection<QuickMenuItem> QuickMenuItems { get; } = new();

    private DateTime _currentCalendarDate = DateTime.Now;

    public MainViewModel(IDiaryService diaryService, IProfileService profileService, ITodoService todoService, IHabitService habitService)
    {
        using var trace = StartupTrace.Measure("MainViewModel..ctor");
        _diaryService = diaryService;
        _profileService = profileService;
        _todoService = todoService;
        _habitService = habitService;
        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;
        
        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        TodayMonthShort = DateTime.Now.ToString("MMM", culture).ToUpper();
        TodayDayNumber = DateTime.Now.ToString("dd");
        
        GenerateCalendar(_currentCalendarDate);
        _ = UpdateCalendarTasksCompletion();
        InitQuickMenuDefault();
        _ = LoadQuickMenuAsync();
    }

    private void InitQuickMenuDefault()
    {
        var defaultItems = new List<QuickMenuItem>
        {
            new QuickMenuItem 
            { 
                Id = "Reading", 
                StrokeColorKey = "Theme_Sage", 
                PathData = "M 3 4 H 7 V 20 H 3 Z M 3 8 H 7 M 10 6 H 14 V 20 H 10 Z M 10 10 H 14 M 17 5 H 21 V 20 H 17 Z M 17 9 H 21",
                Command = OpenReadingTrackerCommand
            },
            new QuickMenuItem 
            { 
                Id = "Moments", 
                StrokeColorKey = "Theme_Berry", 
                PathData = "M 19 14 C 20.49 12.54 22 10.79 22 8.5 A 5.5 5.5 0 0 0 16.5 3 C 14.74 3 13.5 3.5 12 5 C 10.5 3.5 9.26 3 7.5 3 A 5.5 5.5 0 0 0 2 8.5 C 2 10.79 3.51 12.54 5 14 L 12 21.35 Z",
                Command = OpenHappyMomentsCommand
            },
            new QuickMenuItem 
            { 
                Id = "Deeds", 
                StrokeColorKey = "Theme_Amber", 
                FillColorKey = "Theme_Amber",
                PathData = "M 19 14 C 20.49 12.54 22 10.79 22 8.5 A 5.5 5.5 0 0 0 16.5 3 C 14.74 3 13.5 3.5 12 5 C 10.5 3.5 9.26 3 7.5 3 A 5.5 5.5 0 0 0 2 8.5 C 2 10.79 3.51 12.54 5 14 L 12 21.35 Z",
                Command = OpenGoodDeedsCommand
            },
            new QuickMenuItem 
            { 
                Id = "Habits", 
                StrokeColorKey = "Theme_Coral", 
                PathData = "M 7.9 20 A 9 9 0 1 0 4 16.1 L 2 22 Z M 9 12 L 11 14 L 15 10",
                Command = OpenHabitTrackerCommand
            },
            new QuickMenuItem 
            { 
                Id = "Wishlist", 
                StrokeColorKey = "Theme_Berry", 
                UsesUniformAspect = true,
                PathData = "M 32,18 A 14,14 0 0 1 32,46 A 14,14 0 0 1 32,18 M 32,26 A 6,6 0 0 1 32,38 A 6,6 0 0 1 32,26 M 32,10 V 15 M 32,49 V 54 M 10,32 H 15 M 49,32 H 54",
                Command = OpenWishlistCommand
            },
            new QuickMenuItem 
            { 
                Id = "Finance", 
                StrokeColorKey = "Theme_Sage", 
                UsesUniformAspect = true,
                PathData = "M 18 26 C 18 21 21 21 22 21 H 42 C 43 21 46 21 46 26 V 42 C 46 47 43 47 42 47 H 22 C 21 47 18 47 18 42 V 26 Z M 18 26 H 46 M 38 31 H 46 V 39 H 38 C 35 39 35 31 38 31 Z M 41.5 33.5 A 1.5 1.5 0 1 1 41.5 36.5 A 1.5 1.5 0 1 1 41.5 33.5 Z",
                Command = OpenFinanceCommand
            }
        };

        QuickMenuItems.Clear();
        foreach (var item in defaultItems)
        {
            QuickMenuItems.Add(item);
        }
    }

    private async Task LoadQuickMenuAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        
        if (profile.QuickMenuOrder != null && profile.QuickMenuOrder.Count > 0)
        {
            var orderedItems = new List<QuickMenuItem>();
            var currentItems = QuickMenuItems.ToList();
            
            foreach (var id in profile.QuickMenuOrder)
            {
                var item = currentItems.FirstOrDefault(x => x.Id == id);
                if (item != null)
                {
                    orderedItems.Add(item);
                    currentItems.Remove(item);
                }
            }

            foreach (var item in currentItems)
            {
                orderedItems.Add(item);
            }

            // Sync collection
            QuickMenuItems.Clear();
            foreach (var item in orderedItems)
            {
                QuickMenuItems.Add(item);
            }
        }
    }

    private QuickMenuItem? _draggedMenuItem;

    [RelayCommand]
    public void DragMenuStarting(QuickMenuItem item)
    {
        _draggedMenuItem = item;
    }

    [RelayCommand]
    public void DropMenuCompleted()
    {
        _draggedMenuItem = null;
    }

    [RelayCommand]
    public async Task ReorderMenuAsync(QuickMenuItem targetItem)
    {
        if (_draggedMenuItem == null || targetItem == null || _draggedMenuItem == targetItem)
            return;

        int oldIndex = QuickMenuItems.IndexOf(_draggedMenuItem);
        int newIndex = QuickMenuItems.IndexOf(targetItem);

        if (oldIndex < 0 || newIndex < 0)
            return;

        QuickMenuItems.Move(oldIndex, newIndex);

        var profile = await _profileService.GetUserProfileAsync();
        profile.QuickMenuOrder = QuickMenuItems.Select(x => x.Id).ToList();
        await _profileService.SaveUserProfileAsync(profile);
    }

    private void GenerateCalendar(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.GenerateCalendar");
        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        CurrentMonthName = date.ToString("MMMM", culture);
        CurrentYear = date.ToString("yyyy");
        SelectedDateDayName = date.ToString("dddd", culture);
        var calendarDays = new List<CalendarDay>(42);

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        
        int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        if (startDayOfWeek == 0) startDayOfWeek = 7; 

        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        
        for (int i = startDayOfWeek - 1; i > 0; i--)
        {
            calendarDays.Add(new CalendarDay 
            { 
                Day = daysInPrevMonth - i + 1, 
                IsCurrentMonth = false,
                Date = new DateTime(prevMonth.Year, prevMonth.Month, daysInPrevMonth - i + 1)
            });
        }

        for (int i = 1; i <= daysInMonth; i++)
        {
            var currentDate = new DateTime(date.Year, date.Month, i);
            calendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = true,
                IsToday = currentDate.Date == DateTime.Now.Date,
                IsSelected = currentDate.Date == date.Date,
                Date = currentDate
            });
        }

        int totalNeeded = calendarDays.Count <= 35 ? 35 : 42;
        int remainingSlots = totalNeeded - calendarDays.Count;
        var nextMonth = firstDayOfMonth.AddMonths(1);
        for (int i = 1; i <= remainingSlots; i++)
        {
            calendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = false,
                Date = new DateTime(nextMonth.Year, nextMonth.Month, i)
            });
        }

        CalendarDays = calendarDays;
    }

    [ObservableProperty]
    private bool _isPlannerMode;

    [ObservableProperty]
    private bool _isDiaryMode = true; // За замовчуванням увімкнено вкладку записів

    [RelayCommand]
    public async Task SwitchToPlannerModeAsync()
    {
        if (IsPlannerMode)
        {
            return;
        }

        IsPlannerMode = true;
        IsDiaryMode = false;
        await LoadTodosForDateAsync(GetSelectedDate());
    }

    [RelayCommand]
    public void SwitchToDiaryMode()
    {
        IsPlannerMode = false;
        IsDiaryMode = true;
        Todos.Clear();
    }

    [RelayCommand]
    public async Task SelectDate(CalendarDay selectedDay)
    {
        if (selectedDay == null) return;

        await SelectDateAsync(selectedDay.Date);
    }

    [RelayCommand]
    public void ToggleCalendar()
    {
        IsCalendarExpanded = !IsCalendarExpanded;
    }

    [RelayCommand]
    public async Task GoToToday()
    {
        await SelectDateAsync(DateTime.Today);
    }

    [RelayCommand]
    public async Task PreviousMonth()
    {
        await SelectDateAsync(_currentCalendarDate.AddMonths(-1));
    }

    [RelayCommand]
    public async Task NextMonth()
    {
        await SelectDateAsync(_currentCalendarDate.AddMonths(1));
    }

    private async Task SelectDateAsync(DateTime date)
    {
        using var trace = StartupTrace.Measure("MainViewModel.SelectDateAsync");
        
        bool requiresFullRegeneration = _currentCalendarDate.Month != date.Month || _currentCalendarDate.Year != date.Year;
        
        _currentCalendarDate = date.Date;

        if (requiresFullRegeneration || CalendarDays.Count == 0)
        {
            GenerateCalendar(_currentCalendarDate);
            _ = UpdateCalendarTasksCompletion();
        }
        else
        {
            // Оновлюємо виділення без перегенерації всіх UI елементів календаря
            foreach (var day in CalendarDays)
            {
                day.IsSelected = day.Date.Date == date.Date;
            }
            var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
            SelectedDateDayName = date.ToString("dddd", culture);
        }

        await LoadDayContentAsync(_currentCalendarDate);
    }

    private async Task UpdateCalendarTasksCompletion()
    {
        if (CalendarDays.Count == 0) return;

        var firstDay = CalendarDays.First().Date.Date;
        var lastDay = CalendarDays.Last().Date.Date;

        var allTodos = new List<TodoItem>();
        var currentMonth = new DateTime(firstDay.Year, firstDay.Month, 1);
        var lastMonth = new DateTime(lastDay.Year, lastDay.Month, 1);

        while (currentMonth <= lastMonth)
        {
            var monthTodos = await _todoService.GetTodosForMonthAsync(currentMonth.Year, currentMonth.Month);
            allTodos.AddRange(monthTodos);
            currentMonth = currentMonth.AddMonths(1);
        }

        var grouped = allTodos.GroupBy(t => t.TargetDate.Date).ToDictionary(g => g.Key, g => g.ToList());

        var profile = await _profileService.GetUserProfileAsync();
        bool trackingEnabled = profile.IsMenstrualTrackingEnabled && profile.LastPeriodStartDate.HasValue;
        DateTime lastPeriod = trackingEnabled ? profile.LastPeriodStartDate!.Value.Date : DateTime.MinValue;
        int cycleLength = profile.GetNormalizedCycleLength();
        int periodLength = profile.GetNormalizedPeriodLength();

        // Оновлюємо UI строго в головному потоці, щоб не збивати байндинги і не викликати гонку потоків
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var day in CalendarDays)
            {
                // Логіка менструального циклу
                day.IsCycleDay = false;
                day.IsPredictedCycleDay = false;
                day.IsFertileWindow = false;
                if (trackingEnabled)
                {
                    int diff = (day.Date.Date - lastPeriod).Days;
                    if (diff >= 0)
                    {
                        int dayOfCycle = diff % cycleLength;
                        if (dayOfCycle < periodLength)
                        {
                            if (day.Date.Date <= DateTime.Today) day.IsCycleDay = true;
                            else day.IsPredictedCycleDay = true;
                        }
                        
                        // Логіка фертильного вікна (іконка пустишки)
                        int currentCycleDay = dayOfCycle + 1; // 1-based
                        int ovulationDay = cycleLength - 14;
                        int fertileStart = ovulationDay - 5;
                        int fertileEnd = ovulationDay + 1;
                        if (currentCycleDay >= fertileStart && currentCycleDay <= fertileEnd)
                        {
                            day.IsFertileWindow = true;
                        }
                    }
                }

                // Логіка завдань (крапки пріоритетів та прогрес виконання)
                if (grouped.TryGetValue(day.Date.Date, out var dayTodos) && dayTodos.Count > 0)
                {
                    day.HasTasks = true;
                    var incompleteTodos = dayTodos.Where(t => !t.IsCompleted).ToList();
                    
                    int completed = dayTodos.Count(t => t.IsCompleted);
                    day.TaskCompletionPercentage = (double)completed / dayTodos.Count;

                    if (incompleteTodos.Any())
                    {
                        var highest = incompleteTodos.OrderByDescending(t => t.Priority).First();
                        day.PriorityDotColor = highest.Priority switch {
                            TodoPriority.High => Microsoft.Maui.Graphics.Color.FromArgb("#C26D53"), // Coral
                            TodoPriority.Medium => Microsoft.Maui.Graphics.Color.FromArgb("#C9985A"), // Amber
                            _ => Microsoft.Maui.Graphics.Color.FromArgb("#8FA083") // Sage
                        };
                    }
                    else
                    {
                        // Усі завдання виконані
                        day.PriorityDotColor = Microsoft.Maui.Graphics.Color.FromArgb("#929FA7"); // Ocean
                    }
                }
                else
                {
                    day.HasTasks = false;
                    day.TaskCompletionPercentage = 0;
                    day.PriorityDotColor = Microsoft.Maui.Graphics.Colors.Transparent;
                }

                if (day.Date.Date == DateTime.Today)
                {
                    TodayTaskCompletionPercentage = day.TaskCompletionPercentage;
                    TodayProgressColor = day.ProgressColor;
                }
            }
        });
    }

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");
        await LoadEntriesForDateAsync(date);

        var profile = await _profileService.GetUserProfileAsync();
        if (profile.IsMenstrualTrackingEnabled && profile.LastPeriodStartDate.HasValue)
        {
            IsCycleInfoVisible = true;
            DateTime lastPeriod = profile.LastPeriodStartDate.Value.Date;
            int cycleLength = profile.GetNormalizedCycleLength();
            int periodLength = profile.GetNormalizedPeriodLength();
            int diff = (date.Date - lastPeriod).Days;
            int cycleDay = diff % cycleLength;
            if (cycleDay < 0) cycleDay += cycleLength;
            cycleDay += 1; // Відлік днів починається з 1

            CycleDayText = string.Format(Diarion.Resources.Localization.AppResources.CycleDayFormat, cycleDay);

            int ovulationDay = cycleLength - 14;
            int fertileStart = ovulationDay - 5;
            int fertileEnd = ovulationDay + 1;

            if (cycleDay >= fertileStart && cycleDay <= fertileEnd)
            {
                PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbHigh;
                PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#C26D53"); // Coral
            }
            else if (cycleDay >= fertileStart - 2 && cycleDay <= fertileEnd + 2)
            {
                PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbMedium;
                PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#C9985A"); // Amber
            }
            else
            {
                PregnancyProbabilityText = Diarion.Resources.Localization.AppResources.ProbLow;
                PregnancyProbabilityColor = Microsoft.Maui.Graphics.Color.FromArgb("#8FA083"); // Sage
            }
            
            if (CurrentEntry != null) {
                CurrentEntry.CycleDay = cycleDay.ToString();
            }
        }
        else
        {
            IsCycleInfoVisible = false;
        }

        if (IsPlannerMode)
        {
            await LoadTodosForDateAsync(date);
            return;
        }

        Todos.Clear();
    }

    private async Task LoadEntriesForDateAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadEntriesForDateAsync");
        IsBusy = true; // Вимикаємо автозбереження на час завантаження
        CurrentEntry = await _diaryService.GetEntryForDateAsync(date.Date);
        IsBusy = false;
    }

    private async Task LoadTodosForDateAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadTodosForDateAsync");
        var items = await _todoService.GetTodosForDateAsync(date.Date);
        Todos.Clear();
        foreach (var item in items)
        {
            Todos.Add(item);
        }
    }

    private DateTime GetSelectedDate()
    {
        return CalendarDays.FirstOrDefault(day => day.IsSelected)?.Date.Date ?? _currentCalendarDate.Date;
    }

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
        string result = await Shell.Current.DisplayPromptAsync(
            Diarion.Resources.Localization.AppResources.AddHabitPromptTitle,
            Diarion.Resources.Localization.AppResources.AddHabitPromptMessage);
        if (!string.IsNullOrWhiteSpace(result))
        {
            var def = new HabitDefinition { Name = result.Trim(), CreatedAt = GetSelectedDate() };
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
        
        bool confirm = await Shell.Current.DisplayAlertAsync(
            Diarion.Resources.Localization.AppResources.DeleteHabitConfirmTitle,
            string.Format(Diarion.Resources.Localization.AppResources.DeleteHabitConfirmMessage, item.Name),
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
        if (!confirm) return;

        await _habitService.DeleteHabitDefinitionAsync(item.HabitId, GetSelectedDate());
        CurrentEntry.Habits.Remove(item);
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItem todo)
    {
        if (todo == null)
        {
            return;
        }

        try
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            await LoadTodosForDateAsync(GetSelectedDate());
            await UpdateCalendarTasksCompletion();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error deleting todo: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task ToggleTodoCompletionAsync(TodoItem todo)
    {
        if (todo == null) return;
        try
        {
            todo.IsCompleted = !todo.IsCompleted;
            await _todoService.SaveTodoAsync(todo);

            await LoadTodosForDateAsync(GetSelectedDate());
            await UpdateCalendarTasksCompletion();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error toggling todo: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task SaveEntryAsync()
    {
        if (CurrentEntry == null) return;
        
        IsBusy = true;
        try
        {
            await _diaryService.SaveEntryAsync(CurrentEntry);
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
            await LoadDayContentAsync(GetSelectedDate());
            await UpdateCalendarTasksCompletion();
        }
        catch (Exception ex)
        {
            // TODO: Показати повідомлення про помилку користувачу
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
        // Маршрутизація буде налаштована пізніше в AppShell
        await Shell.Current.GoToAsync("DiaryDetail");
    }

    [RelayCommand]
    public async Task GoToNewTodoAsync()
    {
        var selectedDate = Uri.EscapeDataString(GetSelectedDate().ToString("O", CultureInfo.InvariantCulture));
        await Shell.Current.GoToAsync($"TodoDetail?Date={selectedDate}");
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
    public async Task GoToEntryDetailsAsync(DiaryEntry entry)
    {
        if (entry == null)
            return;

        // Передаємо ID запису як параметр
        await Shell.Current.GoToAsync($"DiaryDetail?Id={entry.Id}");
    }

    [RelayCommand]
    public async Task GoToTodoDetailsAsync(TodoItem todo)
    {
        if (todo == null)
            return;

        await Shell.Current.GoToAsync($"TodoDetail?Id={todo.Id}");
    }

    [RelayCommand]
    public async Task OpenHabitTrackerAsync()
    {
        await Shell.Current.GoToAsync("HabitTracker");
    }

    [RelayCommand]
    public async Task OpenReadingTrackerAsync()
    {
        await Shell.Current.GoToAsync("ReadingTracker");
    }

    [RelayCommand]
    public async Task OpenHappyMomentsAsync()
    {
        await Shell.Current.GoToAsync("HappyMoments");
    }

    [RelayCommand]
    public async Task OpenGoodDeedsAsync()
    {
        await Shell.Current.GoToAsync("GoodDeeds");
    }

    [RelayCommand]
    public async Task OpenWishlistAsync()
    {
        await Shell.Current.GoToAsync("Wishlist");
    }

    [RelayCommand]
    public async Task OpenFinanceAsync()
    {
        await Shell.Current.GoToAsync("Finance");
    }
}
