using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

namespace Diarion.ViewModels;

public partial class TodoItemViewModel : ObservableObject
{
    public TodoItem Model { get; }

    public TodoItemViewModel(TodoItem model)
    {
        Model = model;
        _id = model.Id;
        _targetDate = model.TargetDate;
        _hasTime = model.HasTime;
        _targetTime = model.TargetTime;
        _taskDescription = model.TaskDescription;
        _isCompleted = model.IsCompleted;
        _isRecurring = model.RecurringTaskId != null;
        _hasReminder = model.HasReminder;
        _priority = model.Priority;
    }

    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private DateTime _targetDate;

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    private TimeSpan _targetTime;

    [ObservableProperty]
    private string _taskDescription = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    partial void OnIsCompletedChanged(bool value)
    {
        Model.IsCompleted = value;
    }

    /// <summary>Read-only on a row: the series is edited through the task form, never from the list.</summary>
    [ObservableProperty]
    private bool _isRecurring;

    [ObservableProperty]
    private bool _hasReminder;

    [ObservableProperty]
    private TodoPriority _priority;

    public void SyncToModel()
    {
        Model.Id = Id;
        Model.TargetDate = TargetDate;
        Model.HasTime = HasTime;
        Model.TargetTime = TargetTime;
        Model.TaskDescription = TaskDescription;
        Model.IsCompleted = IsCompleted;
        Model.HasReminder = HasReminder;
        Model.Priority = Priority;
    }
}
