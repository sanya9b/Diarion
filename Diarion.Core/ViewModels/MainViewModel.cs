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

    public bool HasEntries => Entries.Count > 0;

    public bool HasNoEntries => !HasEntries;

    [ObservableProperty]
    private List<DiaryEntry> _entries = new();

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
        using var _ = StartupTrace.Measure("MainViewModel..ctor");
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;
        GenerateCalendar(_currentCalendarDate);
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

        int remainingSlots = 42 - calendarDays.Count;
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

    partial void OnEntriesChanged(List<DiaryEntry> value)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
    }

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
        using var _ = StartupTrace.Measure("MainViewModel.SelectDateAsync");
        
        bool requiresFullRegeneration = _currentCalendarDate.Month != date.Month || _currentCalendarDate.Year != date.Year;
        
        _currentCalendarDate = date.Date;

        if (requiresFullRegeneration || CalendarDays.Count == 0)
        {
            GenerateCalendar(_currentCalendarDate);
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

    private async Task LoadDayContentAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("MainViewModel.LoadDayContentAsync");
        await LoadEntriesForDateAsync(date);

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
        Entries = await _diaryService.GetEntriesForDateAsync(date.Date);
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error toggling todo: " + ex.Message);
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
