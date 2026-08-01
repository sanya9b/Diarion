using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Microsoft.Maui.Controls;

namespace Diarion.ViewModels;

[QueryProperty(nameof(TargetDateValue), "Date")]
[QueryProperty(nameof(TodoId), "Id")]
[QueryProperty(nameof(HourValue), "Hour")]
public partial class TodoDetailViewModel : BaseViewModel
{
    private readonly ITodoService _todoService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private DateTime _targetDate = DateTime.Today;
    private TodoItem? _currentTodo;

    public TodoDetailViewModel(ITodoService todoService, INavigationService navigationService, IDialogService dialogService)
    {
        _todoService = todoService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = Diarion.Resources.Localization.AppResources.NewTaskTitle;
        PrioritiesList[1].IsSelected = true; // Medium is default
        UpdateTargetDateDisplay();
    }

    [ObservableProperty]
    public partial string TargetDateValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodoId { get; set; } = string.Empty;

    /// <summary>Set when the form is opened from an empty row of the hour grid, so it starts scheduled.</summary>
    [ObservableProperty]
    public partial string HourValue { get; set; } = string.Empty;

    partial void OnHourValueChanged(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)) return;
        if (hour is < 0 or > 23) return;

        HasTime = true;
        TargetTime = TimeSpan.FromHours(hour);
    }

    [ObservableProperty]
    private string _targetDateDisplay = string.Empty;

    [ObservableProperty]
    private string _taskDescription = string.Empty;

    partial void OnTaskDescriptionChanged(string value) => ApplyParse(value);

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetTimeDisplay))]
    private TimeSpan _targetTime;

    /// <summary>
    /// The time as we want it read — "08:30", zero-padded. TimePicker.Format is honoured on Android and
    /// iOS but not on Windows, where the native control draws its own hour and minute fields and drops
    /// the leading zero. Drawing the text ourselves is the only way the three platforms agree.
    /// </summary>
    public string TargetTimeDisplay => TargetTime.ToString(@"hh\:mm");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecurrenceSummary))]
    private bool _isRecurring;

    [ObservableProperty]
    // Every one of the four, including Daily. Leaving it out meant the Daily chip never repainted, so it
    // stayed lit next to whichever kind was actually chosen.
    [NotifyPropertyChangedFor(nameof(IsRecurrenceDaily))]
    [NotifyPropertyChangedFor(nameof(IsRecurrenceWeekly))]
    [NotifyPropertyChangedFor(nameof(IsRecurrenceInterval))]
    [NotifyPropertyChangedFor(nameof(IsRecurrenceMonthly))]
    [NotifyPropertyChangedFor(nameof(RecurrenceSummary))]
    private RecurrenceKind _recurrenceKind = RecurrenceKind.Daily;

    public bool IsRecurrenceDaily => RecurrenceKind == RecurrenceKind.Daily;
    public bool IsRecurrenceWeekly => RecurrenceKind == RecurrenceKind.Weekly;
    public bool IsRecurrenceInterval => RecurrenceKind == RecurrenceKind.IntervalDays;
    public bool IsRecurrenceMonthly => RecurrenceKind == RecurrenceKind.MonthlyByDay;

    public List<WeekdayToggle> Weekdays { get; } = WeekdayToggle.BuildMondayFirst();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecurrenceSummary))]
    private int _everyNDays = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecurrenceSummary))]
    private int _dayOfMonth = DateTime.Today.Day;

    [ObservableProperty]
    private bool _hasRecurrenceEnd;

    [ObservableProperty]
    private DateTime _recurrenceEndDate = DateTime.Today.AddMonths(1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecurrenceError))]
    private string _recurrenceError = string.Empty;

    public bool HasRecurrenceError => !string.IsNullOrWhiteSpace(RecurrenceError);

    /// <summary>Reads the rule back in words, through the formatter finance and habits already share.</summary>
    public string RecurrenceSummary => IsRecurring ? RecurrenceFormatter.Describe(ComposeRule()) : string.Empty;

    [RelayCommand]
    public void SetRecurrenceKind(string kindName)
    {
        if (!Enum.TryParse<RecurrenceKind>(kindName, out var kind)) return;
        RecurrenceKind = kind;
        _scheduleTouchedByUser = true;
    }

    [RelayCommand]
    public void ToggleWeekday(WeekdayToggle? day)
    {
        if (day == null) return;
        day.IsSelected = !day.IsSelected;
        // Picking days means the user wants specific days, whatever the chips above say.
        RecurrenceKind = RecurrenceKind.Weekly;
        _scheduleTouchedByUser = true;
        OnPropertyChanged(nameof(RecurrenceSummary));
    }

    // --- reading the schedule out of what was typed ---

    private ParsedTaskInput? _parsed;
    private bool _parseDismissed;
    private bool _scheduleTouchedByUser;
    private ScheduleSnapshot? _beforeParse;

    /// <summary>What the parser read, phrased for the user, or empty when it read nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParseHint))]
    private string _parseHint = string.Empty;

    public bool HasParseHint => !string.IsNullOrEmpty(ParseHint);

    private void ApplyParse(string text)
    {
        // Loading an existing task must not re-read its own title: the phrase was already cut out when it
        // was saved, and anything that survived is there because the user chose to keep it.
        if (_parseDismissed || _loadingExisting) return;

        var parsed = TaskInputParser.Parse(text, DateTime.Today);
        if (!parsed.FoundAnything)
        {
            _parsed = null;
            ParseHint = string.Empty;
            return;
        }

        // Taken once, before anything is overwritten, so undo puts back what was there rather than a
        // guess at defaults — which matters most when an existing task is being edited.
        _beforeParse ??= Snapshot();
        _parsed = parsed;

        if (parsed.Recurrence != null && !_scheduleTouchedByUser)
        {
            IsRecurring = true;
            RecurrenceKind = parsed.Recurrence.Kind;
            EveryNDays = Math.Max(1, parsed.Recurrence.EveryN);
            DayOfMonth = Math.Clamp(parsed.Recurrence.DayOfMonth, 1, 31);
            foreach (var day in Weekdays)
            {
                day.IsSelected = parsed.Recurrence.DaysOfWeek.Contains(day.DayOfWeek);
            }
        }

        if (parsed.Date != null && !_scheduleTouchedByUser)
        {
            _targetDate = parsed.Date.Value;
            UpdateTargetDateDisplay();
        }

        if (parsed.TimeOfDay != null && !_scheduleTouchedByUser)
        {
            HasTime = true;
            TargetTime = parsed.TimeOfDay.Value;
            // Naming an hour out loud is asking to be told about it. Typing one into the picker is not,
            // which is why the reminder follows the parser and not the TargetTime property.
            HasReminder = true;
        }

        ParseHint = string.Format(
            Diarion.Resources.Localization.AppResources.ParseHintFormat,
            string.Join(" · ", parsed.Matched));
        OnPropertyChanged(nameof(RecurrenceSummary));
    }

    /// <summary>
    /// The title as it should be stored: with the recognized phrase cut out, unless cutting it would
    /// leave nothing at all. "щовівторка" on its own is a badly named weekly task, which is still better
    /// than a task with no name.
    /// </summary>
    private string DescriptionToSave()
    {
        var typed = TaskDescription.Trim();
        if (_parsed == null || _parseDismissed) return typed;
        if (!string.Equals(_parsed.OriginalText, TaskDescription, StringComparison.Ordinal)) return typed;

        var cut = _parsed.Description.Trim();
        return cut.Length == 0 ? typed : cut;
    }

    /// <summary>Puts the schedule back and stops reading this task's text, leaving it exactly as typed.</summary>
    [RelayCommand]
    public void DismissParse()
    {
        _parseDismissed = true;
        _parsed = null;
        ParseHint = string.Empty;
        _beforeParse?.RestoreTo(this);
        _beforeParse = null;
        OnPropertyChanged(nameof(RecurrenceSummary));
    }

    private ScheduleSnapshot Snapshot() => new()
    {
        IsRecurring = IsRecurring,
        Kind = RecurrenceKind,
        SelectedDays = Weekdays.Where(d => d.IsSelected).Select(d => d.DayOfWeek).ToList(),
        EveryN = EveryNDays,
        DayOfMonth = DayOfMonth,
        HasTime = HasTime,
        TargetTime = TargetTime,
        HasReminder = HasReminder,
        TargetDate = _targetDate
    };

    private sealed class ScheduleSnapshot
    {
        public bool IsRecurring { get; init; }
        public RecurrenceKind Kind { get; init; }
        public List<int> SelectedDays { get; init; } = new();
        public int EveryN { get; init; }
        public int DayOfMonth { get; init; }
        public bool HasTime { get; init; }
        public TimeSpan TargetTime { get; init; }
        public bool HasReminder { get; init; }
        public DateTime TargetDate { get; init; }

        public void RestoreTo(TodoDetailViewModel vm)
        {
            vm.IsRecurring = IsRecurring;
            vm.RecurrenceKind = Kind;
            vm.EveryNDays = EveryN;
            vm.DayOfMonth = DayOfMonth;
            foreach (var day in vm.Weekdays) day.IsSelected = SelectedDays.Contains(day.DayOfWeek);
            vm.HasTime = HasTime;
            vm.TargetTime = TargetTime;
            vm.HasReminder = HasReminder;
            vm._targetDate = TargetDate;
            vm.UpdateTargetDateDisplay();
        }
    }

    [RelayCommand]
    public void IncrementEveryN() => EveryNDays = Math.Min(EveryNDays + 1, 365);

    [RelayCommand]
    public void DecrementEveryN() => EveryNDays = Math.Max(EveryNDays - 1, 1);

    [RelayCommand]
    public void IncrementDayOfMonth() => DayOfMonth = Math.Min(DayOfMonth + 1, 31);

    [RelayCommand]
    public void DecrementDayOfMonth() => DayOfMonth = Math.Max(DayOfMonth - 1, 1);

    [ObservableProperty]
    private bool _hasReminder;

    [ObservableProperty]
    private TodoPriority _selectedPriority = TodoPriority.Medium;

    public List<PriorityItem> PrioritiesList { get; } = new()
    {
        new(TodoPriority.Low, Diarion.Resources.Localization.AppResources.PriorityLow),
        new(TodoPriority.Medium, Diarion.Resources.Localization.AppResources.PriorityMedium),
        new(TodoPriority.High, Diarion.Resources.Localization.AppResources.PriorityHigh)
    };

    [RelayCommand]
    public void SelectPriority(PriorityItem selectedItem)
    {
        if (selectedItem == null) return;

        foreach (var item in PrioritiesList)
        {
            item.IsSelected = false;
        }

        selectedItem.IsSelected = true;
        SelectedPriority = selectedItem.Value;
    }

    partial void OnTargetDateValueChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParse(Uri.UnescapeDataString(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            _targetDate = parsedDate.Date;
        }
        else
        {
            _targetDate = DateTime.Today;
        }

        UpdateTargetDateDisplay();
    }

    async partial void OnTodoIdChanged(string value)
    {
        if (Guid.TryParse(value, out var id))
        {
            await LoadTodoAsync(id);
        }
    }

    private bool _loadingExisting;

    private async Task LoadTodoAsync(Guid id)
    {
        _currentTodo = await _todoService.GetTodoByIdAsync(id);
        if (_currentTodo != null)
        {
            _loadingExisting = true;
            try
            {
            TaskDescription = _currentTodo.TaskDescription;
            IsCompleted = _currentTodo.IsCompleted;
            HasTime = _currentTodo.HasTime;
            TargetTime = _currentTodo.TargetTime;
            IsRecurring = _currentTodo.RecurringTaskId != null;
            if (_currentTodo.RecurringTaskId != null)
            {
                await LoadRecurrenceAsync(_currentTodo.RecurringTaskId.Value);
            }
            HasReminder = _currentTodo.HasReminder;
            
            foreach (var item in PrioritiesList)
            {
                item.IsSelected = item.Value == _currentTodo.Priority;
            }
            SelectedPriority = _currentTodo.Priority;
            
            _targetDate = _currentTodo.TargetDate;
            UpdateTargetDateDisplay();
            Title = Diarion.Resources.Localization.AppResources.EditTaskTitle;
            }
            finally
            {
                _loadingExisting = false;
            }
        }
    }

    private async Task LoadRecurrenceAsync(Guid ruleId)
    {
        var rule = await _todoService.GetRecurringTaskAsync(ruleId);
        if (rule?.Recurrence == null) return;

        RecurrenceKind = rule.Recurrence.Kind;
        // Only read the interval off a rule that actually has one. A daily rule leaves EveryN at 1, and
        // letting that reach the chip makes it read "every 1 day" — the daily chip beside it, worded
        // differently, so neither says what tapping it would do.
        if (rule.Recurrence.Kind == RecurrenceKind.IntervalDays)
        {
            EveryNDays = Math.Max(1, rule.Recurrence.EveryN);
        }
        DayOfMonth = Math.Clamp(rule.Recurrence.DayOfMonth, 1, 31);
        foreach (var day in Weekdays)
        {
            day.IsSelected = rule.Recurrence.DaysOfWeek?.Contains(day.DayOfWeek) == true;
        }

        HasRecurrenceEnd = rule.Recurrence.EndDate != null;
        if (rule.Recurrence.EndDate != null) RecurrenceEndDate = rule.Recurrence.EndDate.Value;

        OnPropertyChanged(nameof(RecurrenceSummary));
    }

    /// <summary>The rule the form currently describes. Built for the summary as well as for saving.</summary>
    private RecurrenceRule ComposeRule() => new()
    {
        Kind = RecurrenceKind,
        DaysOfWeek = Weekdays.Where(d => d.IsSelected).Select(d => d.DayOfWeek).ToList(),
        EveryN = EveryNDays,
        DayOfMonth = DayOfMonth,
        Anchor = _targetDate,
        EndDate = HasRecurrenceEnd ? RecurrenceEndDate.Date : null
    };

    /// <summary>The rule to save, or null to end the series.</summary>
    private RecurrenceRule? BuildRecurrence() => IsRecurring ? ComposeRule() : null;

    /// <summary>
    /// A weekly rule with no day selected never fires, so it would read as "saved" and then quietly do
    /// nothing at all.
    /// </summary>
    private bool RecurrenceIsIncomplete()
        => IsRecurring && RecurrenceKind == RecurrenceKind.Weekly && !Weekdays.Any(d => d.IsSelected);

    [RelayCommand]
    public async Task CloseAsync()
    {
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(TaskDescription))
        {
            return;
        }

        if (RecurrenceIsIncomplete())
        {
            RecurrenceError = Diarion.Resources.Localization.AppResources.RecurrenceDaysRequired;
            return;
        }
        RecurrenceError = string.Empty;

        try
        {
            IsBusy = true;

            // Перевірка ліміту 3-х завдань із високим пріоритетом на день
            if (SelectedPriority == TodoPriority.High)
            {
                var existingTodos = await _todoService.GetTodosForDateAsync(_targetDate);
                
                // Рахуємо скільки ВЖЕ є високих пріоритетів (виключаючи поточне завдання, якщо ми його редагуємо)
                var currentId = _currentTodo?.Id ?? Guid.Empty;
                int highPriorityCount = existingTodos.Count(t => t.Priority == TodoPriority.High && t.Id != currentId);
                
                if (highPriorityCount >= RecurringTaskPlanner.MaxHighPriorityPerDay)
                {
                    IsBusy = false;
                    var title = Diarion.Resources.Localization.AppResources.MaxHighPriorityAlertTitle;
                    var message = Diarion.Resources.Localization.AppResources.MaxHighPriorityAlertMessage;
                    await _dialogService.ShowAlertAsync(title, message, Diarion.Resources.Localization.AppResources.OkButtonLabel);
                    return;
                }
            }

            if (_currentTodo == null)
            {
                _currentTodo = new TodoItem
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.Now
                };
            }

            _currentTodo.TargetDate = _targetDate;
            _currentTodo.HasTime = HasTime;
            _currentTodo.TargetTime = HasTime ? TargetTime : TimeSpan.Zero;
            _currentTodo.TaskDescription = DescriptionToSave();
            _currentTodo.IsCompleted = IsCompleted;
            _currentTodo.Priority = SelectedPriority;
            _currentTodo.HasReminder = HasReminder;

            await _todoService.SaveTodoAsync(_currentTodo);
            await _todoService.SetRecurrenceAsync(_currentTodo.Id, BuildRecurrence());
            await _navigationService.NavigateBackAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateTargetDateDisplay()
    {
        var culture = Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentCulture;
        TargetDateDisplay = _targetDate.ToString("dddd, dd MMMM", culture);
    }
}