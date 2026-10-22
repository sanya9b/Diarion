using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Diarion.Services;

/// <summary>
/// The words a spoken reminder says (spec 19). One place, pure and testable, because the phrase is read
/// by two different mouths — iOS pre-renders it to a sound file when the reminder is scheduled, Android
/// speaks it when the notification arrives — and they have to say the same thing.
/// </summary>
public static class ReminderPhraseBuilder
{
    /// <summary>
    /// The longest task text that is read out. iOS refuses a notification sound longer than thirty
    /// seconds and silently plays the default chime instead, and Ukrainian at the default rate runs at
    /// roughly twelve characters a second; this leaves room for the prefix and a slow voice.
    /// </summary>
    public const int MaxTaskLength = 160;

    // Pictographs, private-use and unpaired surrogates, plus the joiner and variation selector that
    // glue emoji together: a synthesizer either skips them or reads their names aloud («face with
    // tears of joy»), and neither is what the person wrote the task to hear.
    private static readonly Regex Unspeakable = new(@"[\p{So}\p{Co}\p{Cs}\p{Cn}\u200D\uFE0F]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Build(string? taskDescription)
    {
        var task = Clean(taskDescription);
        if (task.Length == 0) return Diarion.Resources.Localization.AppResources.SpokenReminderEmpty;

        return string.Format(CultureInfo.CurrentCulture, Diarion.Resources.Localization.AppResources.SpokenReminderPhrase, task);
    }

    /// <summary>Trims, collapses whitespace, drops what cannot be spoken, and cuts at a word boundary.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var cleaned = Whitespace.Replace(Unspeakable.Replace(text, " "), " ").Trim();
        if (cleaned.Length <= MaxTaskLength) return cleaned;

        var cut = cleaned.LastIndexOf(' ', MaxTaskLength);
        if (cut < MaxTaskLength / 2) cut = MaxTaskLength;
        return cleaned[..cut].TrimEnd(',', ';', ':', '-', '—');
    }
}
