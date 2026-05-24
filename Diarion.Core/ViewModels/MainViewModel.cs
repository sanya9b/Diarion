using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;

    public ObservableCollection<DiaryEntry> Entries { get; } = new();
    public ObservableCollection<CalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<TodoItem> Todos { get; } = new();

    [ObservableProperty]
    private string _currentMonthName = string.Empty;

    [ObservableProperty]
    private string _currentYear = string.Empty;

    [ObservableProperty]
    private string _selectedDateDayName = string.Empty;

    private List<DiaryEntry> _allEntries = new();

    private DateTime _currentCalendarDate = DateTime.Now;

    public MainViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.MyEntriesTitle;
        GenerateCalendar(_currentCalendarDate);
    }

    private void GenerateCalendar(DateTime date)
    {
        CalendarDays.Clear();
        CurrentMonthName = date.ToString("MMMM", CultureInfo.CurrentCulture);
        CurrentYear = date.ToString("yyyy");
        SelectedDateDayName = date.ToString("dddd", CultureInfo.CurrentCulture);

        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        
        int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        if (startDayOfWeek == 0) startDayOfWeek = 7; 

        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        
        for (int i = startDayOfWeek - 1; i > 0; i--)
        {
            CalendarDays.Add(new CalendarDay 
            { 
                Day = daysInPrevMonth - i + 1, 
                IsCurrentMonth = false,
                Date = new DateTime(prevMonth.Year, prevMonth.Month, daysInPrevMonth - i + 1)
            });
        }

        for (int i = 1; i <= daysInMonth; i++)
        {
            var currentDate = new DateTime(date.Year, date.Month, i);
            CalendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = true,
                IsToday = currentDate.Date == DateTime.Now.Date,
                IsSelected = currentDate.Date == date.Date,
                Date = currentDate
            });
        }

        int remainingSlots = 42 - CalendarDays.Count;
        var nextMonth = firstDayOfMonth.AddMonths(1);
        for (int i = 1; i <= remainingSlots; i++)
        {
            CalendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = false,
                Date = new DateTime(nextMonth.Year, nextMonth.Month, i)
            });
        }
    }

    [ObservableProperty]
    private bool _isPlannerMode;

    [ObservableProperty]
    private bool _isDiaryMode = true; // За замовчуванням увімкнено вкладку записів

    [RelayCommand]
    public void SwitchToPlannerMode()
    {
        IsPlannerMode = true;
        IsDiaryMode = false;
        // Можна додати додаткову логіку фільтрації тут, якщо планувальник показує інші дані
    }

    [RelayCommand]
    public void SwitchToDiaryMode()
    {
        IsPlannerMode = false;
        IsDiaryMode = true;
    }

    [RelayCommand]
    public void SelectDate(CalendarDay selectedDay)
    {
        if (selectedDay == null) return;
        
        foreach (var day in CalendarDays)
        {
            day.IsSelected = false;
        }
        selectedDay.IsSelected = true;
        
        SelectedDateDayName = selectedDay.Date.ToString("dddd", CultureInfo.CurrentCulture);
        FilterEntriesByDate(selectedDay.Date);
    }

    [RelayCommand]
    public void PreviousMonth()
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(-1);
        GenerateCalendar(_currentCalendarDate);
    }

    [RelayCommand]
    public void NextMonth()
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(1);
        GenerateCalendar(_currentCalendarDate);
    }

    private async void FilterEntriesByDate(DateTime date)
    {
        Entries.Clear();
        var filtered = _allEntries.Where(e => e.CreatedAt.Date == date.Date).ToList();
        
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }

        Todos.Clear();
        foreach(var entry in filtered)
        {
            var todosForEntry = await _diaryService.GetTodosForEntryAsync(entry.Id);
            foreach(var todo in todosForEntry)
            {
                Todos.Add(todo);
            }
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
            
            // Refresh to maintain sorting
            var selectedDay = CalendarDays.FirstOrDefault(d => d.IsSelected);
            if (selectedDay != null)
            {
                FilterEntriesByDate(selectedDay.Date);
            }
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

        try
        {
            IsBusy = true;
            _allEntries = await _diaryService.GetAllEntriesAsync();
            
            var selectedDay = CalendarDays.FirstOrDefault(d => d.IsSelected);
            if (selectedDay != null)
            {
                FilterEntriesByDate(selectedDay.Date);
            }
            else
            {
                FilterEntriesByDate(DateTime.Now.Date);
            }
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
    public async Task GoToEntryDetailsAsync(DiaryEntry entry)
    {
        if (entry == null)
            return;

        // Передаємо ID запису як параметр
        await Shell.Current.GoToAsync($"DiaryDetail?Id={entry.Id}");
    }
}
