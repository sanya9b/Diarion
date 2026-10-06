using System.Collections.Generic;

namespace Diarion.Models.Ai.Reports;

/// <summary>
/// Everything about one lived period that leaves the device, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A shape of its own rather than the internal statistics classes, for three reasons that are all
/// the same reason. Those classes carry presentation state — hex colours, whole <c>Account</c>
/// objects, "is this worth drawing" flags — none of which belongs in someone else's datacentre.
/// They also change whenever a screen changes, and this is a wire contract. And the promise made on
/// the consent screen is that the user sees exactly what goes: that is only enforceable if one type
/// is both what is shown and what is sent.
/// </para>
/// <para>
/// Every collection is a <c>List</c> in a fixed sort order and every number is already rounded.
/// Both are for determinism: the preview is a proof only if the same period always produces the
/// same bytes, and a dictionary's enumeration order is not a promise anyone made.
/// </para>
/// </remarks>
public sealed class PeriodSnapshot
{
    /// <summary>Lower-case <c>week</c> / <c>month</c> / <c>quarter</c> / <c>year</c>.</summary>
    public string PeriodKind { get; init; } = string.Empty;

    /// <summary><c>yyyy-MM-dd</c>, inclusive.</summary>
    public string Start { get; init; } = string.Empty;

    /// <summary><c>yyyy-MM-dd</c>, inclusive.</summary>
    public string End { get; init; } = string.Empty;

    public int DayCount { get; init; }

    /// <summary>Two-letter code of the app's interface language — the language the report must come back in.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Empty when the user never set one; the model must then avoid naming a currency.</summary>
    public string Currency { get; init; } = string.Empty;

    public SnapshotSleep Sleep { get; init; } = new();

    public SnapshotMood Mood { get; init; } = new();

    public SnapshotTasks Tasks { get; init; } = new();

    public SnapshotFinance Finance { get; init; } = new();

    /// <summary>
    /// Already computed on device by the correlation engine — Pearson with a Benjamini-Hochberg
    /// adjustment across every factor tested in the same pass. Sent as findings, not as data to
    /// re-derive: a language model asked to correlate would produce a number nothing could check.
    /// </summary>
    public List<SnapshotCorrelation> Correlations { get; init; } = new();

    public SnapshotHabits Habits { get; init; } = new();

    /// <summary>
    /// One row per calendar day in the period, including days with nothing written.
    /// </summary>
    /// <remarks>
    /// A week with three entries and a week with seven are different weeks. Dropping the blank days
    /// would hand the model three days and let it call them the week — and a period where someone
    /// stopped writing is exactly the period worth noticing.
    /// </remarks>
    public List<SnapshotDay> Days { get; init; } = new();

    /// <summary>Present only while the user has switched cycle data into the snapshot.</summary>
    public List<SnapshotCycleDay>? Cycle { get; init; }
}

public sealed class SnapshotSleep
{
    public double AverageHours { get; init; }

    /// <summary>Mean of the star ratings, out of <c>DiaryEntry.MaxRating</c>.</summary>
    public double AverageQuality { get; init; }

    public List<SnapshotSleepDay> Daily { get; init; } = new();
}

/// <summary>Nulls mean "not logged" — never zero, which would read as a night without sleep.</summary>
public sealed class SnapshotSleepDay
{
    public string Date { get; init; } = string.Empty;

    public double? Hours { get; init; }

    public double? Quality { get; init; }
}

public sealed class SnapshotMood
{
    /// <summary>Daily average valence on a -2..+2 scale; null on days with nothing logged.</summary>
    public List<SnapshotMoodDay> Daily { get; init; } = new();

    /// <summary>Hours 7..23 as the app records them, and only those with observations.</summary>
    public List<SnapshotMoodHour> ByHour { get; init; } = new();

    public List<SnapshotEmotionCount> Emotions { get; init; } = new();

    public string TopEmotion { get; init; } = string.Empty;
}

public sealed class SnapshotMoodDay
{
    public string Date { get; init; } = string.Empty;

    public double? Valence { get; init; }

    public string? DominantEmotion { get; init; }
}

public sealed class SnapshotMoodHour
{
    public int Hour { get; init; }

    public double Valence { get; init; }

    /// <summary>Observations at this hour across the whole period.</summary>
    public int Observations { get; init; }

    /// <summary>
    /// Distinct calendar days behind those observations. Lower than <see cref="Observations"/>
    /// whenever one day was logged repeatedly, which is what tells a habit apart from an afternoon.
    /// </summary>
    public int Days { get; init; }
}

public sealed class SnapshotEmotionCount
{
    public string Emotion { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class SnapshotTasks
{
    public int Completed { get; init; }

    public int Total { get; init; }
}

public sealed class SnapshotFinance
{
    public decimal Income { get; init; }

    public decimal Expense { get; init; }

    public List<SnapshotAmount> ExpenseByCategory { get; init; } = new();

    public List<SnapshotAmount> IncomeByCategory { get; init; } = new();

    /// <summary>
    /// The same length of window immediately before this one. Null when nothing happened then —
    /// every comparison against zero is "up infinitely", which is arithmetic, not information.
    /// </summary>
    public SnapshotComparison? VersusPrevious { get; init; }
}

public sealed class SnapshotAmount
{
    public string Label { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

public sealed class SnapshotComparison
{
    public decimal PreviousIncome { get; init; }

    public decimal PreviousExpense { get; init; }
}

public sealed class SnapshotCorrelation
{
    /// <summary>Resource key such as <c>FactorSleepDuration</c>, resolved to a label in the prompt.</summary>
    public string Factor { get; init; } = string.Empty;

    /// <summary>Pearson's r, -1..+1.</summary>
    public double Coefficient { get; init; }

    /// <summary>Benjamini-Hochberg adjusted p — the number to judge the finding by.</summary>
    public double AdjustedPValue { get; init; }

    public int SampleSize { get; init; }

    /// <summary>How many days the factor was shifted earlier than the mood it is measured against.</summary>
    public int LagDays { get; init; }
}

public sealed class SnapshotHabits
{
    public List<SnapshotHabit> Good { get; init; } = new();

    public List<SnapshotQuitTracker> Quitting { get; init; } = new();
}

public sealed class SnapshotHabit
{
    public string Name { get; init; } = string.Empty;

    public int CompletedDays { get; init; }

    /// <summary>Days in the period on which this habit was actually scheduled.</summary>
    public int ScheduledDays { get; init; }
}

public sealed class SnapshotQuitTracker
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Days inside this period the user ticked as held out. Not the app's "clean days" figure, which
    /// counts from the last relapse and therefore says something about the whole history rather than
    /// about these seven days.
    /// </summary>
    public int MarkedDays { get; init; }

    /// <summary>Relapses logged inside this period.</summary>
    public int Relapses { get; init; }
}

/// <summary>
/// One day of writing. Empty fields are omitted from the JSON rather than sent as empty strings, so
/// a blank day arrives as a date and nothing else — which is the truth about that day.
/// </summary>
public sealed class SnapshotDay
{
    public string Date { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? Text { get; init; }

    public string? Gratitude { get; init; }

    public string? SoulFood { get; init; }

    public string? Triggers { get; init; }

    public string? SupportForOthers { get; init; }

    /// <summary>
    /// The guided question this day asked, already resolved to text. Sent with the answer because
    /// "так, нарешті" means nothing without knowing what was asked.
    /// </summary>
    public string? Prompt { get; init; }

    public string? PromptAnswer { get; init; }

    public string? SleepNotes { get; init; }

    /// <summary>Present only while the user has switched this field into the snapshot.</summary>
    public string? IntimateLife { get; init; }
}

public sealed class SnapshotCycleDay
{
    public string Date { get; init; } = string.Empty;

    public bool IsPeriodDay { get; init; }

    /// <summary>Resource keys from <c>CycleSymptoms</c>, not free text.</summary>
    public List<string> Symptoms { get; init; } = new();
}
