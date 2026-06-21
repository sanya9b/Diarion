using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public enum TodoPriority
{
    Low,
    Medium,
    High
}

public partial class TodoItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();
    
    // Now tasks are primarily bound to a specific Day. 
    private DateTime _targetDate = DateTime.Today;
    public DateTime TargetDate 
    { 
        get => _targetDate; 
        set
        {
            _targetDate = value.Date;
            OnPropertyChanged(nameof(TargetDate));
        }
    }

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    private TimeSpan _targetTime = DateTime.Now.TimeOfDay;
    
    // Legacy mapping (optional, can be empty)
    [ObservableProperty]
    private Guid _diaryEntryId;
    
    [ObservableProperty]
    private string _taskDescription = string.Empty;
    
    [ObservableProperty]
    private bool _isCompleted;
    
    [ObservableProperty]
    private bool _isDailyRepeat;

    [ObservableProperty]
    private DateTime? _repeatEndDate;

    [ObservableProperty]
    private string? _repeatGroupId;

    [ObservableProperty]
    private bool _hasReminder;
    
    [ObservableProperty]
    private TodoPriority _priority = TodoPriority.Medium;
    
    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;
}
