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

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    private TimeSpan _targetTime;

    [ObservableProperty]
    private bool _isRecurring;

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

    private async Task LoadTodoAsync(Guid id)
    {
        _currentTodo = await _todoService.GetTodoByIdAsync(id);
        if (_currentTodo != null)
        {
            TaskDescription = _currentTodo.TaskDescription;
            IsCompleted = _currentTodo.IsCompleted;
            HasTime = _currentTodo.HasTime;
            TargetTime = _currentTodo.TargetTime;
            IsRecurring = _currentTodo.RecurringTaskId != null;
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
    }

    /// <summary>
    /// The rule the form is asking for, or null to end the series. Daily for now — the kind picker lands
    /// with the rest of the form.
    /// </summary>
    private RecurrenceRule? BuildRecurrence()
        => IsRecurring
            ? new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = _targetDate }
            : null;

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
            _currentTodo.TaskDescription = TaskDescription.Trim();
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