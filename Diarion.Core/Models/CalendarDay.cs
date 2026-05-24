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

    public DateTime Date { get; set; }
}
