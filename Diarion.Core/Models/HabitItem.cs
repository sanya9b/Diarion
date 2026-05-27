using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public partial class HabitItem : ObservableObject
{
    [ObservableProperty]
    private Guid _habitId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;
}