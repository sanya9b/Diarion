using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Picks the day's guided prompt from the mood already recorded for that day. Pure and deterministic:
/// the same day yields the same prompt every time the screen is rebuilt, which is why the date is a
/// parameter and neither <see cref="DateTime.Now"/> nor <see cref="Random"/> appears here.
/// </summary>
public static class PromptSelector
{
    public static PromptCategory SelectCategory(
        Emotion emotion,
        IReadOnlyList<HourMood>? hourlyMood,
        bool gratitudeWritten)
    {
        var valence = MoodAggregate.Valence(emotion, hourlyMood);

        if (valence <= -1) return PromptCategory.CbtReframe;
        if (valence >= 1) return gratitudeWritten ? PromptCategory.Savouring : PromptCategory.EveningGratitude;
        return PromptCategory.OpenReflection;
    }

    /// <summary>
    /// Which position in a category's list the given day lands on. Seeded by the date so the prompt is
    /// stable for the day, and by the category so two categories don't march through their lists in
    /// lockstep. Adding a prompt changes <paramref name="count"/> and so reshuffles days that have not
    /// been answered yet — acceptable, since nothing is written on them.
    /// </summary>
    public static int SelectIndex(DateTime date, PromptCategory category, int count)
    {
        if (count <= 0) return -1;

        var seed = date.Year * 366 + date.DayOfYear + (int)category * 7919;
        return ((seed % count) + count) % count;
    }

    /// <summary>The day's prompt, or null when the chosen category has been emptied by the user.</summary>
    public static GuidedPrompt? Select(
        DateTime date,
        Emotion emotion,
        IReadOnlyList<HourMood>? hourlyMood,
        bool gratitudeWritten,
        PromptLibrary library)
    {
        if (library is null) return null;

        var category = SelectCategory(emotion, hourlyMood, gratitudeWritten);
        var candidates = library.Candidates(category, date);

        var index = SelectIndex(date, category, candidates.Count);
        return index < 0 ? null : candidates[index];
    }

    /// <summary>The next prompt in the same category, wrapping around; used by the shuffle affordance.</summary>
    public static GuidedPrompt? Next(GuidedPrompt? current, DateTime date, PromptLibrary library)
    {
        if (current is null || library is null) return current;

        var candidates = library.Candidates(current.Category, date);
        if (candidates.Count == 0) return current;

        var index = candidates.ToList().FindIndex(p => p.Id == current.Id);

        // A deleted prompt is no longer a candidate; hand back the first one rather than nothing.
        return index < 0 ? candidates[0] : candidates[(index + 1) % candidates.Count];
    }
}
