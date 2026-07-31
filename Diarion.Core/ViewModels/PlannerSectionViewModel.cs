using System;
using System.Collections.ObjectModel;
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

/// <summary>One hour of the day grid. Holds however many tasks fall in it — the row grows rather than
/// squeezing them, because unreadable blocks are worse than an uneven grid.</summary>
public partial class PlannerHourSlot : ObservableObject
{
    public int Hour { get; init; }
    public string Label { get; init; } = string.Empty;
    public ObservableCollection<TodoItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _isEmpty = true;
}

public partial class PlannerSectionViewModel : ObservableObject
{
    /// <summary>
    /// The grid runs 7–23, matching the hourly mood scale so the app has one idea of "a day".
    /// A task outside that window is kept in the nearest edge slot rather than hidden — a task nobody
    /// can see is a task nobody does — and its block always prints its real time.
    /// </summary>
    public const int FirstHour = 7;
    public const int LastHour = 23;

    private readonly ITodoService _todoService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<TodoItemViewModel> Todos { get; } = new();

    /// <summary>Tasks with no time of their own. They keep working exactly as before.</summary>
    public ObservableCollection<TodoItemViewModel> UntimedTodos { get; } = new();

    public ObservableCollection<PlannerHourSlot> HourSlots { get; } = new();

    [ObservableProperty]
    private bool _hasUntimedTodos;

    public PlannerSectionViewModel(
        ITodoService todoService, INavigationService navigationService, IDialogService dialogService)
    {
        _todoService = todoService;
        _navigationService = navigationService;
        _dialogService = dialogService;
    }

    public async Task LoadTodosForDateAsync(DateTime date)
    {
        using var _ = StartupTrace.Measure("PlannerSectionViewModel.LoadTodosForDateAsync");
        var items = await _todoService.GetTodosForDateAsync(date.Date);
        Todos.Clear();
        foreach (var item in items)
        {
            Todos.Add(new TodoItemViewModel(item));
        }

        BuildHourGrid();
    }

    /// <summary>Redistributes <see cref="Todos"/> across the tray and the hour slots.</summary>
    private void BuildHourGrid()
    {
        if (HourSlots.Count == 0)
        {
            for (var hour = FirstHour; hour <= LastHour; hour++)
            {
                HourSlots.Add(new PlannerHourSlot
                {
                    Hour = hour,
                    Label = hour.ToString("00", CultureInfo.InvariantCulture)
                });
            }
        }

        foreach (var slot in HourSlots) slot.Items.Clear();
        UntimedTodos.Clear();

        foreach (var todo in Todos.OrderBy(t => t.TargetTime))
        {
            if (!todo.HasTime)
            {
                UntimedTodos.Add(todo);
                continue;
            }

            HourSlots[SlotIndexFor(todo.TargetTime)].Items.Add(todo);
        }

        foreach (var slot in HourSlots) slot.IsEmpty = slot.Items.Count == 0;
        HasUntimedTodos = UntimedTodos.Count > 0;
    }

    /// <summary>Clamped, so a 06:00 or a 01:00 task lands on an edge row instead of vanishing.</summary>
    internal static int SlotIndexFor(TimeSpan time)
        => Math.Clamp(time.Hours, FirstHour, LastHour) - FirstHour;

    public void ClearTodos()
    {
        Todos.Clear();
        UntimedTodos.Clear();
        foreach (var slot in HourSlots)
        {
            slot.Items.Clear();
            slot.IsEmpty = true;
        }
        HasUntimedTodos = false;
    }

    /// <summary>Creating from an empty row carries the hour into the form, so it opens already scheduled.</summary>
    [RelayCommand]
    private async Task AddAtHourAsync(PlannerHourSlot? slot)
    {
        if (slot == null) return;

        var date = Todos.Count > 0 ? Todos[0].TargetDate : DateTime.Today;
        var encoded = Uri.EscapeDataString(date.ToString("O", CultureInfo.InvariantCulture));
        await _navigationService.NavigateToAsync($"TodoDetail?Date={encoded}&Hour={slot.Hour}");
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItemViewModel todo)
    {
        if (todo == null)
            return;

        try
        {
            // A one-off just goes. A row that belongs to a series is two different requests wearing the
            // same gesture, and guessing which one was meant is how a whole series disappears by accident.
            if (todo.Model.RecurringTaskId != null)
            {
                var thisOne = Diarion.Resources.Localization.AppResources.DeleteThisOccurrenceOption;
                var wholeSeries = Diarion.Resources.Localization.AppResources.DeleteWholeSeriesOption;

                var choice = await _dialogService.ShowActionSheetAsync(
                    Diarion.Resources.Localization.AppResources.DeleteRecurringTaskTitle,
                    Diarion.Resources.Localization.AppResources.CancelButtonLabel,
                    thisOne, wholeSeries);

                if (choice == null) return;

                if (choice == wholeSeries)
                {
                    await _todoService.DeleteRecurringTaskAsync(todo.Model.RecurringTaskId.Value);
                    Todos.Remove(todo);
                    WeakReferenceMessenger.Default.Send(new TodoChangedMessage(todo.TargetDate));
                    return;
                }
            }

            await _todoService.DeleteTodoAsync(todo.Id);
            Todos.Remove(todo);
            WeakReferenceMessenger.Default.Send(new TodoChangedMessage(todo.TargetDate));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error deleting todo: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task ToggleTodoCompletionAsync(TodoItemViewModel todo)
    {
        if (todo == null) return;
        try
        {
            todo.IsCompleted = !todo.IsCompleted;
            todo.SyncToModel();
            await _todoService.SaveTodoAsync(todo.Model);
            await LoadTodosForDateAsync(todo.TargetDate);
            WeakReferenceMessenger.Default.Send(new TodoChangedMessage(todo.TargetDate));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error toggling todo: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task GoToTodoDetailsAsync(TodoItemViewModel todo)
    {
        if (todo == null)
            return;

        await _navigationService.NavigateToAsync($"TodoDetail?Id={todo.Id}");
    }
}
