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
    
    // Зовнішній ключ для зв'язку зі щоденниковим записом
    public Guid DiaryEntryId { get; set; }
    
    public string TaskDescription { get; set; } = string.Empty;
    
    public bool IsCompleted { get; set; }
    
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
