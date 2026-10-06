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

    /// <summary>Cost of a single unit avoided (e.g. one cigarette) — for the money-saved estimate.</summary>
    public decimal CostPerUnit { get; set; }

    /// <summary>Units consumed per day before quitting.</summary>
    public double UnitsPerDay { get; set; }

    /// <summary>Logged relapses; the most recent one resets the clean-time counter.</summary>
    public List<RelapseEvent> Relapses { get; set; } = new();

    /// <summary>Optional daily reminder time-of-day. Null means no reminder.</summary>
    public TimeSpan? ReminderTime { get; set; }
}

/// <summary>A single relapse: when it happened and an optional note.</summary>
public class RelapseEvent
{
    public DateTime Date { get; set; }
    public string Note { get; set; } = string.Empty;
}