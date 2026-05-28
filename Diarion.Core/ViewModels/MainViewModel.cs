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
    private bool _isCalendarExpanded = true;

    [ObservableProperty]
    private List<CalendarDay> _calendarDays = new();

    [ObservableProperty]
    private List<TodoItem> _todos = new();

    [ObservableProperty]
    private string _currentMonthName = string.Empty;

    [ObservableProperty]
    private string _currentYear = string.Empty;

    [ObservableProperty]
    private string _selectedDateDayName = string.Empty;

    private DateTime _currentCalendarDate = DateTime.Now;

    public MainViewModel(IDiaryService diaryService)
    {
        using var trace = StartupTrace.Measure("MainViewModel..ctor");
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;
        GenerateCalendar(_currentCalendarDate);
        _ = UpdateCalendarTasksCompletion();
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
        Todos = new List<TodoItem>();
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
            var monthTodos = await _diaryService.GetTodosForMonthAsync(currentMonth.Year, currentMonth.Month);
            allTodos.AddRange(monthTodos);
            currentMonth = currentMonth.AddMonths(1);
        }

        var grouped = allTodos.GroupBy(t => t.TargetDate.Date).ToDictionary(g => g.Key, g => g.ToList());

        var profile = await _diaryService.GetUserProfileAsync();
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
            }
        });
    }

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");
        await LoadEntriesForDateAsync(date);

        var profile = await _diaryService.GetUserProfileAsync();
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

        Todos = new List<TodoItem>();
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
        Todos = await _diaryService.GetTodosForDateAsync(date.Date);
    }

    private DateTime GetSelectedDate()
    {
        return CalendarDays.FirstOrDefault(day => day.IsSelected)?.Date.Date ?? _currentCalendarDate.Date;
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
            await _diaryService.AddHabitDefinitionAsync(def);
            
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

        await _diaryService.DeleteHabitDefinitionAsync(item.HabitId, GetSelectedDate());
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
            await _diaryService.DeleteTodoAsync(todo.Id);
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
            await _diaryService.SaveTodoAsync(todo);

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
}
