using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Microsoft.Maui.Controls;

namespace Diarion.ViewModels;

[QueryProperty(nameof(TargetDateValue), "Date")]
[QueryProperty(nameof(TodoId), "Id")]
public partial class TodoDetailViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private DateTime _targetDate = DateTime.Today;
    private TodoItem? _currentTodo;

    public TodoDetailViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.NewTaskTitle;
        PrioritiesList[1].IsSelected = true; // Medium is default
        UpdateTargetDateDisplay();
    }

    [ObservableProperty]
    public partial string TargetDateValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodoId { get; set; } = string.Empty;

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
    private bool _isDailyRepeat;

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
        _currentTodo = await _diaryService.GetTodoByIdAsync(id);
        if (_currentTodo != null)
        {
            TaskDescription = _currentTodo.TaskDescription;
            IsCompleted = _currentTodo.IsCompleted;
            HasTime = _currentTodo.HasTime;
            TargetTime = _currentTodo.TargetTime;
            IsDailyRepeat = _currentTodo.IsDailyRepeat;
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

    [RelayCommand]
    public async Task CloseAsync()
    {
        await Shell.Current.GoToAsync("..");
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
                var existingTodos = await _diaryService.GetTodosForDateAsync(_targetDate);
                
                // Рахуємо скільки ВЖЕ є високих пріоритетів (виключаючи поточне завдання, якщо ми його редагуємо)
                var currentId = _currentTodo?.Id ?? Guid.Empty;
                int highPriorityCount = existingTodos.Count(t => t.Priority == TodoPriority.High && t.Id != currentId);
                
                if (highPriorityCount >= 3)
                {
                    IsBusy = false;
                    var title = Diarion.Resources.Localization.AppResources.MaxHighPriorityAlertTitle;
                    var message = Diarion.Resources.Localization.AppResources.MaxHighPriorityAlertMessage;
                    await Shell.Current.DisplayAlertAsync(title, message, Diarion.Resources.Localization.AppResources.OkButtonLabel);
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
            _currentTodo.IsDailyRepeat = IsDailyRepeat;
            _currentTodo.HasReminder = HasReminder;

            await _diaryService.SaveTodoAsync(_currentTodo);
            await Shell.Current.GoToAsync("..");
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