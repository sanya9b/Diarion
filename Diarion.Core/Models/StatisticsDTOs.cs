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

public enum CorrelationStrength
{
    Negligible,
    Weak,
    Moderate,
    Strong
}

/// <summary>
/// A statistical association between a daily factor (e.g. sleep duration) and mood valence.
/// Coefficient is Pearson's r (-1..+1); Confidence is 1..5 (significance from a Fisher z-test);
/// it is a correlation, not proven causation.
/// </summary>
public class MoodCorrelation
{
    public string FactorKey { get; set; } = string.Empty;
    public double Coefficient { get; set; }
    public CorrelationStrength Strength { get; set; }
    public int Confidence { get; set; }
    public int SampleSize { get; set; }
    public int LagDays { get; set; }
}