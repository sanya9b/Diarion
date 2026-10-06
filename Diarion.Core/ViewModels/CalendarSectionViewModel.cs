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
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;
    private readonly ITodoService _todoService;
    private readonly IDispatcherService _dispatcher;

    [ObservableProperty]
    private bool _isCalendarExpanded = false;

    [ObservableProperty]
    private List<CalendarDay> _calendarDays = new();

    /// <summary>Mirrors the day cell in CalendarView.xaml — its height, and the gap under it.</summary>
    private const double DayCellHeight = 52;
    private const double DayCellSpacing = 8;

    /// <summary>
    /// The grid's height, stated rather than measured. A wrapping FlexLayout with a percentage Basis
    /// measures itself a row taller than it lays out, and <c>AlignContent="Start"</c> packs the weeks at
    /// the top — so the surplus showed up as a band of empty space between the last week and the day name
    /// underneath, which read as a padding nobody could find. The trailing gap under the final row is
    /// subtracted: it is spacing between weeks, and there is no week after the last one.
    /// </summary>
    [ObservableProperty]
    private double _calendarGridHeight;

    [ObservableProperty]
    private string _currentMonthName = string.Empty;

    [ObservableProperty]
    private string _currentYear = string.Empty;

    /// <summary>
    /// The chosen day, named and dated — "середа, 13 серпня". The weekday alone was ambiguous the moment
    /// the calendar collapsed: every Wednesday reads the same, and there was nothing on screen to say
    /// which one you had landed on.
    /// </summary>
    [ObservableProperty]
    private string _selectedDateLabel = string.Empty;

    [ObservableProperty]
    private string _todayMonthShort = string.Empty;

    [ObservableProperty]
    private string _todayDayNumber = string.Empty;

    [ObservableProperty]
    private double _todayTaskCompletionPercentage;

    public DateTime CurrentCalendarDate { get; private set; } = DateTime.Now;

    public CalendarSectionViewModel(
        ICalendarService calendarService,
        ICycleLogService cycleLogService,
        IProfileService profileService,
        ITodoService todoService,
        IDispatcherService dispatcher)
    {
        _calendarService = calendarService;
        _cycleLogService = cycleLogService;
        _profileService = profileService;
        _todoService = todoService;
        _dispatcher = dispatcher;

        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        TodayMonthShort = DateTime.Now.ToString("MMM", culture).ToUpper();
        TodayDayNumber = DateTime.Now.ToString("dd");

        // Search opens a day by asking for it. Going through the calendar rather than loading the
        // content directly is what keeps the header, the highlighted day and the content agreeing —
        // otherwise the screen shows June under a heading that says August.
        WeakReferenceMessenger.Default.Register<NavigateToDateMessage>(this, (r, m) =>
        {
            _ = SelectDateInternalAsync(m.Date);
        });
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
        SelectedDateLabel = date.ToString("dddd, d MMMM", culture);
        
        CalendarDays = _calendarService.GenerateCalendarDays(date);

        // Five weeks or six, depending on how the month falls.
        var weeks = (int)Math.Ceiling(CalendarDays.Count / 7.0);
        CalendarGridHeight = weeks * (DayCellHeight + DayCellSpacing) - DayCellSpacing;
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
            SelectedDateLabel = date.ToString("dddd, d MMMM", culture);
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
        var history = await BuildCycleHistoryAsync(profile);

        _dispatcher.InvokeOnMainThread(() =>
        {
            foreach (var day in CalendarDays)
            {
                UpdateDayTasksCompletion(day, grouped.GetValueOrDefault(day.Date.Date), profile, history);
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
        var history = await BuildCycleHistoryAsync(profile);

        _dispatcher.InvokeOnMainThread(() =>
        {
            UpdateDayTasksCompletion(dayToUpdate, dayTodos, profile, history);
        });
    }

    /// <summary>
    /// Read and derived once per repaint. The per-day method below runs forty-two times, and rebuilding
    /// the episode history inside it would redo the same work for every cell in the grid.
    /// </summary>
    private async Task<CycleHistory> BuildCycleHistoryAsync(UserProfile? profile)
    {
        if (profile?.IsCycleTrackingActive != true) return CycleHistory.Empty;

        return CycleForecastCalculator.BuildHistory(await _cycleLogService.GetMarkedDatesAsync());
    }

    private void UpdateDayTasksCompletion(CalendarDay day, List<TodoItem>? dayTodos, UserProfile profile, CycleHistory cycleHistory)
    {
        var forecast = CycleForecastCalculator.Describe(cycleHistory, profile, day.Date, DateTime.Today);
        day.IsCycleDay = forecast.IsPeriodDay;
        day.IsPredictedCycleDay = forecast.IsPredictedPeriodDay;
        day.IsFertileWindow = forecast.IsFertileWindowEstimate;

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
