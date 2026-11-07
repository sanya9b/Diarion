using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// How close the data is to supporting any correlation at all.
/// <para>
/// <paramref name="PairedDays"/> is the best any single factor manages — days where that factor and a
/// mood were both recorded. It is deliberately the best rather than an average: the first insight
/// appears as soon as one factor clears the bar, so that is what the user is waiting on.
/// </para>
/// <para>
/// <paramref name="BestFactorKey"/> names that factor, so a screen can say what to log today for
/// the day to count, not only how many days are left. Null while nothing has paired at all.
/// </para>
/// </summary>
public readonly record struct CorrelationReadiness(int PairedDays, int RequiredDays, string? BestFactorKey = null)
{
    public bool IsReady => PairedDays >= RequiredDays;

    public int DaysRemaining => Math.Max(0, RequiredDays - PairedDays);
}

public interface ICorrelationService
{
    /// <summary>
    /// Computes on-device correlations between daily factors and mood over <paramref name="range"/>.
    /// <paramref name="lagDays"/> shifts the factor earlier than the mood (e.g. 1 = yesterday's factor
    /// vs today's mood). Only factors with enough paired days are returned, ranked by strength. These
    /// are associations, not proven causes.
    /// </summary>
    Task<IReadOnlyList<MoodCorrelation>> GetMoodCorrelationsAsync(StatsRange range, int lagDays = 0);

    /// <summary>
    /// The same pass measured against the day's energy (arousal) instead of its valence. A separate
    /// method rather than a flag so that it is also a separate Benjamini-Hochberg family: the energy
    /// hypotheses never compete with the mood ones, and adding this axis moves no existing finding.
    /// Readiness is shared with <see cref="GetReadinessAsync"/> — a day with a mood has an energy.
    /// </summary>
    Task<IReadOnlyList<MoodCorrelation>> GetEnergyCorrelationsAsync(StatsRange range, int lagDays = 0);

    /// <summary>
    /// How much more logging it takes before any correlation can be reported. Exists so the screen
    /// can say "six days of fourteen" instead of showing nothing at all for a fortnight — which is
    /// the fortnight during which people decide whether the app is worth keeping.
    /// </summary>
    Task<CorrelationReadiness> GetReadinessAsync(StatsRange range, int lagDays = 0);
}
