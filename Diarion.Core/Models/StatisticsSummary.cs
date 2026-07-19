using System;
using System.Collections.Generic;

namespace Diarion.Models;

public class SleepDataPoint
{
    public DateTime Date { get; set; }
    public TimeSpan Duration { get; set; }
    public double Quality { get; set; }
}

public class SleepStatistics
{
    public TimeSpan AverageSleepDuration { get; set; }
    public double AverageSleepQuality { get; set; }
    public List<SleepDataPoint> DailyData { get; set; } = new();
}

/// <summary>A single day on the mood trend line. <see cref="HasData"/> is false for days with no
/// logged emotion, so the chart can break the line across gaps instead of inventing a value.</summary>
public class MoodTrendPoint
{
    public DateTime Date { get; set; }
    public double Valence { get; set; }
    public bool HasData { get; set; }

    /// <summary>The day's representative emotion (mode of that day's entries), used to color the heatmap.</summary>
    public Emotion DominantEmotion { get; set; } = Emotion.None;
}

/// <summary>One cell of the Year-in-Pixels heatmap: a calendar day with a resolved color.</summary>
public class MoodHeatDay
{
    public DateTime Date { get; set; }
    public string ColorHex { get; set; } = "#D0D3D4";
    public bool HasData { get; set; }
}

public class MoodStatistics
{
    public Dictionary<Emotion, int> EmotionCounts { get; set; } = new();
    public Emotion TopEmotion { get; set; } = Emotion.None;

    /// <summary>Daily average mood valence (-2..+2), one point per calendar day in the window.</summary>
    public List<MoodTrendPoint> DailyTrend { get; set; } = new();
}

public class TodoStatistics
{
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public double CompletionPercentage => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;
}

/// <summary>Completion history for a single good habit over a date window, for strength/streak/heatmap.</summary>
public class HabitCompletionHistory
{
    public Guid HabitId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public HabitSchedule Schedule { get; set; } = new();
    public HashSet<DateTime> CompletedDates { get; set; } = new();
}

public class CategoryStatItem
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public string ColorHex { get; set; } = "#929FA7"; // Default Ocean color
}

public class FinanceStatistics
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public List<CategoryStatItem> ExpenseByCategory { get; set; } = new();
    public List<CategoryStatItem> IncomeByCategory { get; set; } = new();
    
    public bool IsEmpty => TotalIncome == 0 && TotalExpense == 0;
    public bool IsNotEmpty => !IsEmpty;
}
