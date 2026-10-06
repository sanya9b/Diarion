using System;

namespace Diarion.Models;

public class HabitDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Localization key for the built-in default habits (e.g. "HabitWater"). When set, the
    /// display name is resolved from resources in the current UI language instead of the stored
    /// <see cref="Name"/>. Empty for user-created habits, whose <see cref="Name"/> is used as-is.
    /// </summary>
    public string ResourceKey { get; set; } = string.Empty;

    public int Order { get; set; } = int.MaxValue;
    public DateTime CreatedAt { get; set; } = DateTime.Today;
    public DateTime? DeletedAt { get; set; }

    /// <summary>When this habit is expected. Defaults to daily; legacy rows without a stored schedule
    /// deserialize to this default too. The habit's own window is <see cref="CreatedAt"/>/<see cref="DeletedAt"/>,
    /// so the rule's own Anchor and EndDate are left unset.</summary>
    public RecurrenceRule Schedule { get; set; } = new();

    /// <summary>
    /// Optional weekly quota ("any 3 days a week"). Null — the normal case — means the schedule alone
    /// decides. When set, strength and streak are counted in weeks and the schedule stays open on every day.
    /// </summary>
    public CompletionTarget? Target { get; set; }

    /// <summary>Optional daily reminder time-of-day. Null means no reminder.</summary>
    public TimeSpan? ReminderTime { get; set; }

    /// <summary>Whether the habit is expected on <paramref name="date"/> (null-safe over <see cref="Schedule"/>).</summary>
    public bool IsScheduledOn(DateTime date) => (Schedule ?? new RecurrenceRule()).IsOccurrenceOn(date);
}
