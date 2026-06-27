using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Diagnostics;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class PlannerSectionViewModel : ObservableObject
{
    private readonly ITodoService _todoService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<TodoItemViewModel> Todos { get; } = new();

    public PlannerSectionViewModel(ITodoService todoService, INavigationService navigationService)
    {
        _todoService = todoService;
        _navigationService = navigationService;
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
    }

    public void ClearTodos()
    {
        Todos.Clear();
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItemViewModel todo)
    {
        if (todo == null)
            return;

        try
        {
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
