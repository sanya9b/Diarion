using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Microsoft.Maui.Controls;

namespace Diarion.ViewModels;

[QueryProperty(nameof(EntryId), "Id")]
public partial class DiaryDetailViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private readonly ITodoService _todoService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private DiaryEntry _currentEntry = new();

    [RelayCommand]
    public async Task CloseAsync()
    {
        await _navigationService.NavigateBackAsync();
    }

    public DiaryDetailViewModel(
        IDiaryService diaryService, 
        ITodoService todoService,
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _diaryService = diaryService;
        _todoService = todoService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = Diarion.Resources.Localization.AppResources.NewEntryTitle;
        SelectedEmotionItem = EmotionsList[0];
        SelectedPriorityItem = PrioritiesList[1]; // За замовчуванням Medium
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExistingEntry))]
    public partial string EntryId { get; set; } = string.Empty;

    public bool IsExistingEntry => !string.IsNullOrEmpty(EntryId);

    [ObservableProperty]
    public partial string EntryTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EntryContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Emotion SelectedEmotion { get; set; } = Emotion.None;

    public List<EmotionItem> EmotionsList { get; } = new()
    {
        new(Emotion.None, Diarion.Resources.Localization.AppResources.EmotionNone),
        new(Emotion.Happy, Diarion.Resources.Localization.AppResources.EmotionHappy),
        new(Emotion.Calm, Diarion.Resources.Localization.AppResources.EmotionCalm),
        new(Emotion.Anxious, Diarion.Resources.Localization.AppResources.EmotionAnxious),
        new(Emotion.Sad, Diarion.Resources.Localization.AppResources.EmotionSad),
        new(Emotion.Angry, Diarion.Resources.Localization.AppResources.EmotionAngry)
    };

    [ObservableProperty]
    public partial EmotionItem? SelectedEmotionItem { get; set; }

    partial void OnSelectedEmotionItemChanged(EmotionItem? value)
    {
        if (value != null)
        {
            SelectedEmotion = value.Value;
        }
    }

    // --- TODO LIST LOGIC ---
    public ObservableCollection<TodoItem> Todos { get; } = new();

    [ObservableProperty]
    public partial string NewTodoDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TodoPriority SelectedPriority { get; set; } = TodoPriority.Medium;

    public List<PriorityItem> PrioritiesList { get; } = new()
    {
        new(TodoPriority.Low, Diarion.Resources.Localization.AppResources.PriorityLow),
        new(TodoPriority.Medium, Diarion.Resources.Localization.AppResources.PriorityMedium),
        new(TodoPriority.High, Diarion.Resources.Localization.AppResources.PriorityHigh)
    };

    [ObservableProperty]
    public partial PriorityItem? SelectedPriorityItem { get; set; }

    partial void OnSelectedPriorityItemChanged(PriorityItem? value)
    {
        if (value != null)
        {
            SelectedPriority = value.Value;
        }
    }

    [RelayCommand]
    public async Task AddTodoAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTodoDescription))
            return;

        if (!IsExistingEntry)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertWarning,
                Diarion.Resources.Localization.AppResources.SaveEntryFirstWarning,
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
            return;
        }

        var todo = new TodoItem
        {
            DiaryEntryId = _currentEntry.Id,
            TaskDescription = NewTodoDescription,
            IsCompleted = false,
            Priority = SelectedPriority
        };

        try
        {
            await _todoService.SaveTodoAsync(todo);
            NewTodoDescription = string.Empty;
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding task: {ex.Message}");
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
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating task: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItem todo)
    {
        if (todo == null) return;
        
        try
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting task: {ex.Message}");
        }
    }

    private async Task ReloadTodosAsync()
    {
        var todos = await _todoService.GetTodosForDateAsync(_currentEntry.CreatedAt);
        Todos.Clear();
        foreach (var t in todos)
        {
            Todos.Add(t);
        }
    }
    // ----------------------

    // Автоматично викликається, коли приходить параметр Id
    partial void OnEntryIdChanged(string value)
    {
        if (Guid.TryParse(value, out Guid id))
        {
            _ = LoadEntryAsync(id);
        }
    }

    private async Task LoadEntryAsync(Guid id)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            _currentEntry = await _diaryService.GetEntryByIdAsync(id);
            
            if (_currentEntry != null)
            {
                EntryTitle = _currentEntry.Title;
                EntryContent = _currentEntry.Content;
                SelectedEmotion = _currentEntry.Emotion;
                SelectedEmotionItem = EmotionsList.Find(e => e.Value == _currentEntry.Emotion) ?? EmotionsList[0];
                Title = Diarion.Resources.Localization.AppResources.EditEntryTitle;
                
                await ReloadTodosAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading entry: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EntryContent))
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertWarning,
                Diarion.Resources.Localization.AppResources.AlertEmptyContent,
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
            return;
        }

        try
        {
            IsBusy = true;

            _currentEntry.Title = EntryTitle;
            _currentEntry.Content = EntryContent;
            _currentEntry.Emotion = SelectedEmotion;
            // CreatedAt та Id вже ініціалізовані або збережені з існуючого

            await _diaryService.SaveEntryAsync(_currentEntry);
            
            // Якщо це був щойно створений запис, ми маємо повідомити UI, що тепер він "існуючий",
            // щоб користувач міг одразу додавати тудушки без перезаходу на сторінку.
            if (!IsExistingEntry)
            {
                EntryId = _currentEntry.Id.ToString();
            }

            await _navigationService.NavigateBackAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertError,
                Diarion.Resources.Localization.AppResources.AlertSaveError,
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
            System.Diagnostics.Debug.WriteLine($"Error saving: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (_currentEntry == null || _currentEntry.Id == Guid.Empty)
            return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle,
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg,
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
        
        if (confirm)
        {
            try
            {
                IsBusy = true;
                await _diaryService.DeleteEntryAsync(_currentEntry.Id);
                await _navigationService.NavigateBackAsync();
            }
            catch (Exception ex)
            {
                await _dialogService.ShowAlertAsync(
                    Diarion.Resources.Localization.AppResources.AlertError,
                    Diarion.Resources.Localization.AppResources.AlertDeleteError,
                    Diarion.Resources.Localization.AppResources.OkButtonLabel);
                System.Diagnostics.Debug.WriteLine($"Error deleting: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}

public record EmotionItem(Emotion Value, string DisplayName);

public partial class PriorityItem : ObservableObject
{
    public TodoPriority Value { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;

    public PriorityItem(TodoPriority value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}
