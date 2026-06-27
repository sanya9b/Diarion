using System;

namespace Diarion.Models;

public enum TodoPriority
{
    Low,
    Medium,
    High
}

public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    private DateTime _targetDate = DateTime.Today;
    public DateTime TargetDate 
    { 
        get => _targetDate; 
        set => _targetDate = value.Date;
    }

    public bool HasTime { get; set; }

    public TimeSpan TargetTime { get; set; } = DateTime.Now.TimeOfDay;
    
    public Guid DiaryEntryId { get; set; }
    
    public string TaskDescription { get; set; } = string.Empty;
    
    public bool IsCompleted { get; set; }
    
    public bool IsDailyRepeat { get; set; }

    public DateTime? RepeatEndDate { get; set; }

    public string? RepeatGroupId { get; set; }

    public bool HasReminder { get; set; }
    
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
