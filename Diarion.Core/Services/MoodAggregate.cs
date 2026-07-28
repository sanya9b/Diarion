using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// The single rule for reading a day's mood: the hourly scale wins where it has data, the day-level
/// scalar is the fallback. Every consumer — prompts, statistics, correlations, export — goes through
/// here so they cannot drift apart. Pure and deterministic.
/// </summary>
public static class MoodAggregate
{
    /// <summary>
    /// The mood observations for a day. <see cref="Emotion.None"/> means "not recorded" and never
    /// becomes an observation, so a day with nothing logged yields an empty sequence rather than a
    /// misleading neutral reading.
    /// </summary>
    public static IReadOnlyList<Emotion> Observations(Emotion scalar, IReadOnlyList<HourMood>? hourly)
    {
        if (hourly is { Count: > 0 })
        {
            var logged = hourly.Where(h => h.Mood != Emotion.None).Select(h => h.Mood).ToList();
            if (logged.Count > 0) return logged;
        }

        return scalar == Emotion.None ? System.Array.Empty<Emotion>() : new[] { scalar };
    }

    public static bool HasAny(Emotion scalar, IReadOnlyList<HourMood>? hourly) =>
        Observations(scalar, hourly).Count > 0;

    /// <summary>Mean valence across the day's observations; 0 when nothing was recorded.</summary>
    public static double Valence(Emotion scalar, IReadOnlyList<HourMood>? hourly)
    {
        var observations = Observations(scalar, hourly);
        return observations.Count == 0 ? 0 : observations.Average(e => e.ToValence());
    }

    /// <summary>
    /// The day's most frequent emotion, ties broken by enum order so the result is stable across runs.
    /// <see cref="Emotion.None"/> when nothing was recorded.
    /// </summary>
    public static Emotion Dominant(Emotion scalar, IReadOnlyList<HourMood>? hourly)
    {
        var observations = Observations(scalar, hourly);
        if (observations.Count == 0) return Emotion.None;

        return observations
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => (int)g.Key)
            .First().Key;
    }
}
