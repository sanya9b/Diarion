using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Diagnostics;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class PlannerSectionViewModel : ObservableObject
{
    private readonly ITodoService _todoService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<TodoItem> Todos { get; } = new();

    public event Action<DateTime>? OnTodoChanged;

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
            Todos.Add(item);
        }
    }

    public void ClearTodos()
    {
        Todos.Clear();
    }

    [RelayCommand]
    public async Task DeleteTodoAsync(TodoItem todo)
    {
        if (todo == null)
            return;

        try
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            Todos.Remove(todo);
            OnTodoChanged?.Invoke(todo.TargetDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error deleting todo: " + ex.Message);
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
            await LoadTodosForDateAsync(todo.TargetDate);
            OnTodoChanged?.Invoke(todo.TargetDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error toggling todo: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task GoToTodoDetailsAsync(TodoItem todo)
    {
        if (todo == null)
            return;

        await _navigationService.NavigateToAsync($"TodoDetail?Id={todo.Id}");
    }
}
