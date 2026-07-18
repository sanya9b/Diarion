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
}
