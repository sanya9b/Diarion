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

    /// <summary>Day-level summary; the fallback when no hours were logged.</summary>
    public Emotion Emotion { get; set; }

    /// <summary>Per-hour moods. Read through <c>MoodAggregate</c>, never directly.</summary>
    public List<HourMood> HourlyMood { get; set; } = new();

    /// <summary>
    /// Share of the day's habits that were ticked, or null when none were configured. A fraction and
    /// not a count, because the number of habits a person tracks drifts over time and a raw count
    /// would read that drift as a change in behaviour. Reduced during projection rather than carried
    /// as a list — the correlation engine wants one number per day, not the roster.
    /// </summary>
    public double? HabitCompletion { get; set; }

    /// <summary>How many of the five meal slots were ticked. Zero also means "not filled in".</summary>
    public int MealsLogged { get; set; }
}

public class TodoStatsDto
{
    public DateTime TargetDate { get; set; }
    public bool IsCompleted { get; set; }
}

/// <summary>
/// One answered guided prompt. A projection rather than the whole entry: the history screen shows
/// every answer the user has ever written, and hydrating each full diary document to read two strings
/// is the kind of read spec 11 exists to prevent.
/// </summary>
public class PromptAnswerDto
{
    /// <summary>Id of the diary entry the answer lives in, so the row can open that day.</summary>
    public Guid EntryId { get; set; }

    public DateTime Date { get; set; }

    /// <summary>
    /// Either a <c>GuidedPrompt.Id</c> or a legacy resource key — resolve through
    /// <c>PromptLibrary.Find</c>, never by parsing it here.
    /// </summary>
    public string PromptReference { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;
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

    /// <summary>
    /// One to five dots, read off <see cref="AdjustedPValue"/> rather than the raw p. With a wide
    /// factor set the raw value overstates the evidence, and the dots are what the user actually sees.
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>Two-sided p for this factor on its own, before accounting for the others.</summary>
    public double PValue { get; set; }

    /// <summary>
    /// Benjamini-Hochberg adjusted p across every factor tested in the same pass — the number to
    /// judge a finding by, because the app tests many factors against the same mood series at once.
    /// </summary>
    public double AdjustedPValue { get; set; }

    public int SampleSize { get; set; }
    public int LagDays { get; set; }
}