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

    /// <summary>
    /// The <see cref="RecurringTask"/> that materialized this row, or null for a one-off. Nullable so rows
    /// written before repeats moved onto a rule deserialize to null without a migration. Together with
    /// <see cref="TargetDate"/> it also identifies an occurrence, which is what stops a series producing
    /// the same day twice, and it is what pins the row against being dragged forward by auto-migration.
    /// </summary>
    public Guid? RecurringTaskId { get; set; }

    public bool HasReminder { get; set; }
    
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
