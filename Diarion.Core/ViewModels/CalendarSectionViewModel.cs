using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Diagnostics;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class CalendarSectionViewModel : ObservableObject
{
    private readonly ICalendarService _calendarService;
    private readonly IMenstrualCycleService _menstrualCycleService;
    private readonly IProfileService _profileService;
    private readonly ITodoService _todoService;

    [ObservableProperty]
    private bool _isCalendarExpanded = false;

    [ObservableProperty]
    private List<CalendarDay> _calendarDays = new();

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

    public DateTime CurrentCalendarDate { get; private set; } = DateTime.Now;

    public CalendarSectionViewModel(
        ICalendarService calendarService,
        IMenstrualCycleService menstrualCycleService,
        IProfileService profileService,
        ITodoService todoService)
    {
        _calendarService = calendarService;
        _menstrualCycleService = menstrualCycleService;
        _profileService = profileService;
        _todoService = todoService;

        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        TodayMonthShort = DateTime.Now.ToString("MMM", culture).ToUpper();
        TodayDayNumber = DateTime.Now.ToString("dd");
    }

    public void Initialize()
    {
        GenerateCalendar(CurrentCalendarDate);
        _ = UpdateCalendarTasksCompletionAsync();
    }

    private void GenerateCalendar(DateTime date)
    {
        using var _ = StartupTrace.Measure("CalendarSectionViewModel.GenerateCalendar");
        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        CurrentMonthName = date.ToString("MMMM", culture);
        CurrentYear = date.ToString("yyyy");
        SelectedDateDayName = date.ToString("dddd", culture);
        
        CalendarDays = _calendarService.GenerateCalendarDays(date);
    }

    [RelayCommand]
    public async Task SelectDateAsync(CalendarDay selectedDay)
    {
        if (selectedDay == null) return;
        await SelectDateInternalAsync(selectedDay.Date);
    }

    [RelayCommand]
    public void ToggleCalendar()
    {
        IsCalendarExpanded = !IsCalendarExpanded;
    }

    [RelayCommand]
    public async Task GoToTodayAsync()
    {
        await SelectDateInternalAsync(DateTime.Today);
    }

    [RelayCommand]
    public async Task PreviousMonthAsync()
    {
        await SelectDateInternalAsync(CurrentCalendarDate.AddMonths(-1));
    }

    [RelayCommand]
    public async Task NextMonthAsync()
    {
        await SelectDateInternalAsync(CurrentCalendarDate.AddMonths(1));
    }

    public async Task SelectDateInternalAsync(DateTime date)
    {
        using var trace = StartupTrace.Measure("CalendarSectionViewModel.SelectDateInternalAsync");
        
        bool requiresFullRegeneration = CurrentCalendarDate.Month != date.Month || CurrentCalendarDate.Year != date.Year;
        
        CurrentCalendarDate = date.Date;

        if (requiresFullRegeneration || CalendarDays.Count == 0)
        {
            GenerateCalendar(CurrentCalendarDate);
            _ = UpdateCalendarTasksCompletionAsync();
        }
        else
        {
            foreach (var day in CalendarDays)
            {
                day.IsSelected = day.Date.Date == date.Date;
            }
            var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
            SelectedDateDayName = date.ToString("dddd", culture);
        }

        WeakReferenceMessenger.Default.Send(new DateSelectedMessage(CurrentCalendarDate));
    }

    public async Task UpdateCalendarTasksCompletionAsync()
    {
        if (CalendarDays.Count == 0) return;

        var firstDay = CalendarDays.First().Date.Date;
        var lastDay = CalendarDays.Last().Date.Date;

        var allTodos = await _todoService.GetTodosForDateRangeAsync(firstDay, lastDay);
        var grouped = allTodos.GroupBy(t => t.TargetDate.Date).ToDictionary(g => g.Key, g => g.ToList());
        var profile = await _profileService.GetUserProfileAsync();

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var day in CalendarDays)
            {
                UpdateDayTasksCompletion(day, grouped.GetValueOrDefault(day.Date.Date), profile);
            }
        });
    }

    public async Task UpdateCalendarTasksForDayAsync(DateTime date)
    {
        var targetDate = date.Date;
        var dayToUpdate = CalendarDays.FirstOrDefault(d => d.Date.Date == targetDate);
        if (dayToUpdate == null) return;

        var dayTodos = await _todoService.GetTodosForDateAsync(targetDate);
        var profile = await _profileService.GetUserProfileAsync();

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateDayTasksCompletion(dayToUpdate, dayTodos, profile);
        });
    }

    private void UpdateDayTasksCompletion(CalendarDay day, List<TodoItem>? dayTodos, UserProfile profile)
    {
        var cycleInfo = _menstrualCycleService.GetCycleInfoForDate(day.Date, profile);
        day.IsCycleDay = cycleInfo.IsPeriodDay;
        day.IsPredictedCycleDay = cycleInfo.IsPredictedPeriodDay;
        day.IsFertileWindow = cycleInfo.IsFertileWindow;

        if (dayTodos != null && dayTodos.Count > 0)
        {
            day.HasTasks = true;
            var incompleteTodos = dayTodos.Where(t => !t.IsCompleted).ToList();
            
            int completed = dayTodos.Count(t => t.IsCompleted);
            day.TaskCompletionPercentage = (double)completed / dayTodos.Count;

            if (incompleteTodos.Any())
            {
                var highest = incompleteTodos.OrderByDescending(t => t.Priority).First();
                day.HighestPriority = highest.Priority;
            }
            else
            {
                day.HighestPriority = null; // Completed state or no priority
            }
        }
        else
        {
            day.HasTasks = false;
            day.TaskCompletionPercentage = 0;
            day.HighestPriority = null;
        }

        if (day.Date.Date == DateTime.Today)
        {
            TodayTaskCompletionPercentage = day.TaskCompletionPercentage;
        }
    }

    public DateTime GetSelectedDate()
    {
        return CalendarDays.FirstOrDefault(day => day.IsSelected)?.Date.Date ?? CurrentCalendarDate.Date;
    }
}
