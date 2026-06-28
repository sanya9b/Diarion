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

public class MoodStatistics
{
    public Dictionary<Emotion, int> EmotionCounts { get; set; } = new();
    public Emotion TopEmotion { get; set; } = Emotion.None;
}

public class TodoStatistics
{
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public double CompletionPercentage => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;
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
