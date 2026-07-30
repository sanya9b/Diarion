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

/// <summary>One hour slot of the day on the hour-of-day mood profile. <see cref="HasData"/> is false for
/// hours never logged in the window, so the chart can leave the slot empty instead of drawing a bar at
/// zero — which would read as "neutral" rather than "nothing recorded".</summary>
public class MoodHourPoint
{
    public int Hour { get; set; }

    /// <summary>Mean valence (-2..+2) of every observation logged at this hour; 0 when there are none.</summary>
    public double Valence { get; set; }

    /// <summary>Observations at this hour across the whole window, not days.</summary>
    public int Count { get; set; }

    /// <summary>Distinct calendar days that contributed at least one observation at this hour. Lower than
    /// <see cref="Count"/> whenever a single day was logged repeatedly, which is what tells a real pattern
    /// apart from one thorough afternoon.</summary>
    public int DayCount { get; set; }

    public bool HasData { get; set; }
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

    /// <summary>Average mood valence per hour of day, always one point per hour 7..23.</summary>
    public List<MoodHourPoint> HourlyProfile { get; set; } = new();
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
    public RecurrenceRule Schedule { get; set; } = new();
    public CompletionTarget? Target { get; set; }
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

    /// <summary>Income and expense per bucket over the window, for the diverging chart.</summary>
    public Diarion.Services.FinanceTrendReport Trend { get; set; } = new();

    /// <summary>The same window measured against the one immediately before it.</summary>
    public Diarion.Services.FinanceComparisonReport Comparison { get; set; } = new();

    /// <summary>Per-account flow. Empty when a single account is already selected.</summary>
    public List<Diarion.Services.FinanceAccountReportRow> AccountBreakdown { get; set; } = new();

    public bool IsEmpty => TotalIncome == 0 && TotalExpense == 0;
    public bool IsNotEmpty => !IsEmpty;
}
