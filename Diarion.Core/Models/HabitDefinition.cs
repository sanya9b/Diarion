using System;

namespace Diarion.Models;

public class HabitDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; } = int.MaxValue;
    public DateTime CreatedAt { get; set; } = DateTime.Today;
    public DateTime? DeletedAt { get; set; }
}
