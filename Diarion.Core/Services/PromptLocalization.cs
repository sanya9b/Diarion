using System.Globalization;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>
/// Picks which of a prompt's two stored texts to show. A user who writes a prompt in one language only
/// should still see it after switching the UI language, so the other language is the fallback rather
/// than an empty card.
/// </summary>
public static class PromptLocalization
{
    public static string ResolveText(GuidedPrompt? prompt)
    {
        if (prompt is null) return string.Empty;

        var culture = AppResources.Culture ?? CultureInfo.CurrentUICulture;
        var preferUkrainian = culture.TwoLetterISOLanguageName == "uk";

        var preferred = preferUkrainian ? prompt.TextUk : prompt.TextEn;
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;

        var other = preferUkrainian ? prompt.TextEn : prompt.TextUk;
        if (!string.IsNullOrWhiteSpace(other)) return other;

        // Both literals blank on a seeded row — fall back to where its text came from rather than
        // rendering nothing. Unreachable for user prompts, which cannot be saved empty.
        if (!string.IsNullOrEmpty(prompt.ResourceKey))
            return AppResources.ResourceManager.GetString(prompt.ResourceKey, culture) ?? string.Empty;

        return string.Empty;
    }
}
