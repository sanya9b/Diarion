using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Models;

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
    private double _taskCompletionPercentage;

    [ObservableProperty]
    private Color _progressColor = Colors.Transparent;

    public DateTime Date { get; set; }

    partial void OnTaskCompletionPercentageChanged(double value)
    {
        if (value > 0.0)
            ProgressColor = Color.FromArgb("#C26D53"); // Coral
        else
            ProgressColor = Colors.Transparent;
    }
}
