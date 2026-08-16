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

    /// <summary>
    /// When the task stops, for one that occupies a stretch of the day rather than a moment in it — a
    /// 13:00–16:00 block. Null is a point task, which is why it is nullable rather than a
    /// <c>HasEndTime</c>/<c>EndTime</c> pair: a row written before this existed deserializes to null, which
    /// already means "no range", so no migration is needed to say so. Exclusive, the way "до 16:00" is
    /// meant — a block ending at 16:00 is not happening during the 16th hour.
    /// </summary>
    public TimeSpan? EndTime { get; set; }

    public Guid DiaryEntryId { get; set; }
    
    public string TaskDescription { get; set; } = string.Empty;
    
    public bool IsCompleted { get; set; }
    
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
