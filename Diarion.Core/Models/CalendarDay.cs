using System;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private string _progressColorHex = "#929FA7"; // Ocean as default partial color

    public DateTime Date { get; set; }

    public double[] TaskProgressDashArray
    {
        get
        {
            // Circumference for a Radius=17 (Width 40, Margin 2 -> size 36 -> R=18, stroke center R=17)
            // StrokeThickness=2 -> values are in multiples of 2. 
            // Total length = (2 * PI * 17) / 2 = 53.407
            double filled = TaskCompletionPercentage * 53.407;
            return new double[] { filled, 1000 };
        }
    }

    partial void OnTaskCompletionPercentageChanged(double value)
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(TaskProgressDashArray)));
        if (value >= 1.0)
            ProgressColorHex = "#10B981"; // Green for completed
        else if (value <= 0.0)
            ProgressColorHex = "Transparent";
        else
            ProgressColorHex = "#929FA7"; // Ocean
    }
}
