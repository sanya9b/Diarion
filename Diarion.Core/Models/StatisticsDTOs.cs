using System;

namespace Diarion.Models;

public class DiaryEntryStatsDto
{
    public DateTime Date { get; set; }
    public TimeSpan? SleepStart { get; set; }
    public TimeSpan? SleepEnd { get; set; }
    public int SleepQuality { get; set; }
    public bool HasSleepStart => SleepStart.HasValue;
    public bool HasSleepEnd => SleepEnd.HasValue;
    public Emotion Emotion { get; set; }
}

public class TodoStatsDto
{
    public DateTime TargetDate { get; set; }
    public bool IsCompleted { get; set; }
}