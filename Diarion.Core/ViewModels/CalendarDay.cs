using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;

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
    [NotifyPropertyChangedFor(nameof(ShowsFertileWindow))]
    private bool _isFertileWindow;

    /// <summary>
    /// Whether the calendar draws the fertile-window marker on this day.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="IsFertileWindow"/> deliberately. That one answers what the
    /// forecast said and stays true whether or not anything is drawn — a day inside the window is
    /// inside it regardless of what v1 shows. This one answers whether it is shown, and the reason
    /// it currently is not lives in <see cref="Diarion.Services.CycleDisplay"/>.
    ///
    /// Gating the drawn property rather than the computed one is what keeps
    /// CalendarSectionViewModel honest: it can go on assigning what the forecast returned instead of
    /// assigning false to a property named IsFertileWindow on days that are in the fertile window.
    /// </remarks>
    public bool ShowsFertileWindow => CycleDisplay.FertileWindowMarkerOffered && IsFertileWindow;

    [ObservableProperty]
    private TodoPriority? _highestPriority;

    [ObservableProperty]
    private double _taskCompletionPercentage;

    public DateTime Date { get; set; }
}
