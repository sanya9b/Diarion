using System;
using System.Collections.Generic;

namespace Diarion.Models;

public class HarmfulHabitTracker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HarmfulHabitName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DateTime> MarkedDays { get; set; } = new();
}