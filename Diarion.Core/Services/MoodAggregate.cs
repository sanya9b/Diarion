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
    /// <summary>First hour of the day the hourly mood scale covers.</summary>
    public const int FirstHour = 7;

    /// <summary>Last hour of the day the hourly mood scale covers.</summary>
    public const int LastHour = 23;

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

    /// <summary>
    /// The day's hour-stamped observations, for readings that need to know <em>when</em>. Unlike
    /// <see cref="Observations"/> this deliberately has no scalar fallback: the day-level scalar has no
    /// hour, so a scalar-only day contributes nothing rather than being smeared across the clock. Hours
    /// outside <see cref="FirstHour"/>..<see cref="LastHour"/> are dropped — the scale never produced
    /// them, so they can only come from imported or legacy data.
    /// </summary>
    public static IReadOnlyList<HourMood> HourlyObservations(IReadOnlyList<HourMood>? hourly)
    {
        if (hourly is not { Count: > 0 }) return System.Array.Empty<HourMood>();

        return hourly
            .Where(h => h.Mood != Emotion.None && h.Hour >= FirstHour && h.Hour <= LastHour)
            .ToList();
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
