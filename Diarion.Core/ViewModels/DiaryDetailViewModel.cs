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
    private DiaryEntry _currentEntry = new();

    private static Page? GetActivePage()
    {
        if (Shell.Current?.CurrentPage is Page currentPage)
        {
            return currentPage;
        }

        if (Application.Current?.Windows.Count > 0)
        {
            return Application.Current.Windows[0].Page;
        }

        return null;
    }

    private static Task ShowAlertAsync(string title, string message)
    {
        var page = GetActivePage();
        return page is null
            ? Task.CompletedTask
            : page.DisplayAlertAsync(title, message, Diarion.Resources.Localization.AppResources.OkButtonLabel);
    }

    private static Task<bool> ShowConfirmationAsync(string title, string message, string accept, string cancel)
    {
        var page = GetActivePage();
        return page is null
            ? Task.FromResult(false)
            : page.DisplayAlertAsync(title, message, accept, cancel);
    }

    private static async Task NavigateBackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var page = GetActivePage();
        if (page?.Navigation?.NavigationStack.Count > 1)
        {
            await page.Navigation.PopAsync();
        }
    }

    [RelayCommand]
    public async Task CloseAsync()
    {
        await NavigateBackAsync();
    }

    public DiaryDetailViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
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
            await ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertWarning,
                Diarion.Resources.Localization.AppResources.SaveEntryFirstWarning);
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
            await _diaryService.SaveTodoAsync(todo);
            NewTodoDescription = string.Empty;
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка додавання завдання: {ex.Message}");
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
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка оновлення завдання: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItem todo)
    {
        if (todo == null) return;
        
        try
        {
            await _diaryService.DeleteTodoAsync(todo.Id);
            await ReloadTodosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка видалення завдання: {ex.Message}");
        }
    }

    private async Task ReloadTodosAsync()
    {
        var todos = await _diaryService.GetTodosForDateAsync(_currentEntry.CreatedAt);
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
            System.Diagnostics.Debug.WriteLine($"Помилка завантаження запису: {ex.Message}");
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
            await ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertWarning,
                Diarion.Resources.Localization.AppResources.AlertEmptyContent);
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

            await NavigateBackAsync();
        }
        catch (Exception ex)
        {
            await ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AlertError,
                Diarion.Resources.Localization.AppResources.AlertSaveError);
            System.Diagnostics.Debug.WriteLine($"Помилка збереження: {ex.Message}");
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

        bool confirm = await ShowConfirmationAsync(
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
                await NavigateBackAsync();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync(
                    Diarion.Resources.Localization.AppResources.AlertError,
                    Diarion.Resources.Localization.AppResources.AlertDeleteError);
                System.Diagnostics.Debug.WriteLine($"Помилка видалення: {ex.Message}");
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
