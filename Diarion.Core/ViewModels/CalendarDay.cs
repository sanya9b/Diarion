using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

namespace Diarion.ViewModels;

public partial class CalendarDay : ObservableObject
{
    [ObservableProperty]
    private int _day;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _hasTasks;

    [ObservableProperty]
    private bool _isCycleDay;

    [ObservableProperty]
    private bool _isPredictedCycleDay;

    [ObservableProperty]
    private bool _isFertileWindow;

    [ObservableProperty]
    private TodoPriority? _highestPriority;

    [ObservableProperty]
    private double _taskCompletionPercentage;

    public DateTime Date { get; set; }
}
