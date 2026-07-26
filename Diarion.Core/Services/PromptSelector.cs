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

    public static string SelectKey(
        DateTime date,
        Emotion emotion,
        IReadOnlyList<HourMood>? hourlyMood,
        bool gratitudeWritten)
    {
        var category = SelectCategory(emotion, hourlyMood, gratitudeWritten);
        var keys = PromptCatalog.KeysFor(category);

        // Seeded by the date so the prompt is stable for the day, and by the category so two categories
        // don't march through their lists in lockstep.
        var seed = date.Year * 366 + date.DayOfYear + (int)category * 7919;
        var index = ((seed % keys.Count) + keys.Count) % keys.Count;
        return keys[index];
    }
}
