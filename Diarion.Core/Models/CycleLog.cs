using System;
using System.Collections.Generic;

namespace Diarion.Models;

/// <summary>
/// A single day the user marked as a period day. One row per day rather than an episode with a start
/// and an end: marking is then an idempotent insert or delete of one row, and splitting or merging an
/// episode falls out of grouping consecutive dates instead of needing its own editing UI.
/// </summary>
public class CycleLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>
    /// Symptom keys for this day (see <see cref="CycleSymptoms"/>), resolved to localized labels by the
    /// view. Keys rather than text so the wording can change and a second language can be added without
    /// a migration — the same reason the built-in prompts kept a ResourceKey.
    /// </summary>
    public List<string> Symptoms { get; set; } = new();

    /// <summary>
    /// The row exists only to carry symptoms; the day is not part of a period. Deliberately phrased as the
    /// negative: a <c>bool IsPeriodDay</c> would deserialize to <c>false</c> on every row written before
    /// this field existed and silently un-mark the user's entire history.
    /// </summary>
    public bool IsSymptomOnly { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool HasSymptoms => Symptoms is { Count: > 0 };
}

/// <summary>The symptoms offered in the log. Keys are stable; the labels live in the resx.</summary>
public static class CycleSymptoms
{
    public const string Cramps = "CycleSymptomCramps";
    public const string Headache = "CycleSymptomHeadache";
    public const string Bloating = "CycleSymptomBloating";
    public const string Fatigue = "CycleSymptomFatigue";
    public const string MoodSwings = "CycleSymptomMoodSwings";
    public const string Tenderness = "CycleSymptomTenderness";
    public const string Acne = "CycleSymptomAcne";
    public const string Cravings = "CycleSymptomCravings";

    public static readonly string[] All =
    {
        Cramps, Headache, Bloating, Fatigue, MoodSwings, Tenderness, Acne, Cravings
    };
}
