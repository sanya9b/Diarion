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
    
    // Now tasks are primarily bound to a specific Day. 
    public DateTime TargetDate { get; set; } = DateTime.Now.Date;
    public bool HasTime { get; set; }
    public TimeSpan TargetTime { get; set; } = DateTime.Now.TimeOfDay;
    
    // Legacy mapping (optional, can be empty)
    public Guid DiaryEntryId { get; set; }
    
    public string TaskDescription { get; set; } = string.Empty;
    
    public bool IsCompleted { get; set; }
    
    public bool IsDailyRepeat { get; set; }
    public bool HasReminder { get; set; }
    
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
