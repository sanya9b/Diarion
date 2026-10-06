using System.Globalization;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>
/// Resolves the display names of the built-in default habits so they follow the current UI
/// language instead of the value that happened to be stored when the database was first seeded.
/// </summary>
public static class HabitLocalization
{
    /// <summary>
    /// Resource keys for the 5 built-in default habits, in their seed order.
    /// </summary>
    public static readonly string[] DefaultHabitResourceKeys =
    {
        "HabitPhysicalActivity",
        "HabitWater",
        "HabitVitamins",
        "HabitReading",
        "HabitSocial",
    };

    /// <summary>
    /// Returns the localized name for a default habit, or <c>null</c> for user-created habits
    /// (which carry no <see cref="HabitDefinition.ResourceKey"/> and keep their stored name).
    /// </summary>
    public static string? ResolveName(HabitDefinition? definition)
    {
        if (definition is null || string.IsNullOrEmpty(definition.ResourceKey))
            return null;

        var culture = AppResources.Culture ?? CultureInfo.CurrentUICulture;
        return AppResources.ResourceManager.GetString(definition.ResourceKey, culture);
    }
}
