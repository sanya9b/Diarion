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
