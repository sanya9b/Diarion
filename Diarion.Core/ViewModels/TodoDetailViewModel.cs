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
        SelectedPriorityItem = PrioritiesList[1];
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
    private TodoPriority _selectedPriority = TodoPriority.Medium;

    [ObservableProperty]
    private PriorityItem? _selectedPriorityItem;

    public List<PriorityItem> PrioritiesList { get; } = new()
    {
        new(TodoPriority.Low, Diarion.Resources.Localization.AppResources.PriorityLow),
        new(TodoPriority.Medium, Diarion.Resources.Localization.AppResources.PriorityMedium),
        new(TodoPriority.High, Diarion.Resources.Localization.AppResources.PriorityHigh)
    };

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
            SelectedPriorityItem = PrioritiesList.FirstOrDefault(p => p.Value == _currentTodo.Priority) ?? PrioritiesList[1];
            _targetDate = _currentTodo.TargetDate;
            UpdateTargetDateDisplay();
            Title = Diarion.Resources.Localization.AppResources.EditTaskTitle;
        }
    }

    partial void OnSelectedPriorityItemChanged(PriorityItem? value)
    {
        if (value != null)
        {
            SelectedPriority = value.Value;
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

            if (_currentTodo == null)
            {
                _currentTodo = new TodoItem
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.Now
                };
            }

            _currentTodo.TargetDate = _targetDate;
            _currentTodo.TargetTime = DateTime.Now.TimeOfDay;
            _currentTodo.TaskDescription = TaskDescription.Trim();
            _currentTodo.IsCompleted = IsCompleted;
            _currentTodo.Priority = SelectedPriority;

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