using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

namespace Diarion.ViewModels;

public partial class HabitItemViewModel : ObservableObject
{
    public HabitItem Model { get; }

    public HabitItemViewModel(HabitItem model)
    {
        Model = model;
        _habitId = model.HabitId;
        _name = model.Name;
        _isCompleted = model.IsCompleted;
    }

    [ObservableProperty]
    private Guid _habitId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    partial void OnIsCompletedChanged(bool value)
    {
        Model.IsCompleted = value;
    }

    public void SyncToModel()
    {
        Model.HabitId = HabitId;
        Model.Name = Name;
        Model.IsCompleted = IsCompleted;
    }
}
