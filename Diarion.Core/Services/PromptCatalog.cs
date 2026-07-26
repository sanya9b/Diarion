using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>
/// The built-in library of guided reflection prompts. Prompts live in the resource files rather than
/// the database on purpose: a seeded copy would freeze the text in whatever language was active at
/// seed time, which is exactly the defect the default habits needed a backfill to undo.
/// </summary>
public static class PromptCatalog
{
    private static readonly IReadOnlyDictionary<PromptCategory, string[]> Keys =
        new Dictionary<PromptCategory, string[]>
        {
            [PromptCategory.CbtReframe] = Numbered("PromptCbt", 10),
            [PromptCategory.Savouring] = Numbered("PromptSavour", 10),
            [PromptCategory.OpenReflection] = Numbered("PromptOpen", 10),
            [PromptCategory.EveningGratitude] = Numbered("PromptGratitude", 10),
        };

    public static IReadOnlyList<string> KeysFor(PromptCategory category) => Keys[category];

    public static IEnumerable<PromptCategory> Categories => Keys.Keys;

    public static IEnumerable<string> AllKeys => Keys.Values.SelectMany(k => k);

    /// <summary>
    /// Which category a stored key belongs to, or null if the key is unknown. Lets callers tell whether
    /// a prompt already on an entry still suits the day's mood without re-deriving it.
    /// </summary>
    public static PromptCategory? CategoryOf(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var (category, keys) in Keys)
        {
            if (keys.Contains(key)) return category;
        }
        return null;
    }

    /// <summary>The next prompt in the same category, wrapping around; used by the shuffle affordance.</summary>
    public static string Next(string key)
    {
        var category = CategoryOf(key);
        if (category is null) return key;

        var keys = Keys[category.Value];
        var index = Array.IndexOf(keys, key);
        return keys[(index + 1) % keys.Length];
    }

    /// <summary>
    /// Resolves a prompt key to display text in the current UI language. Falls back to the key itself
    /// so a typo surfaces visibly instead of rendering an empty card.
    /// </summary>
    public static string ResolveText(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        var culture = AppResources.Culture ?? CultureInfo.CurrentUICulture;
        return AppResources.ResourceManager.GetString(key, culture) ?? key;
    }

    private static string[] Numbered(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i:00}").ToArray();
}
