using System;

namespace Diarion.Models;

public class HabitItem
{
    public Guid HabitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }
}
