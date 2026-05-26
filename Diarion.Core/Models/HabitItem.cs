using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public partial class HabitItem : ObservableObject
{
    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _note = string.Empty;
}