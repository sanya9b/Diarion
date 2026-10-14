using System;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

/// <summary>
/// The user-facing name of a correlation factor, shared by the statistics screen and the home-screen
/// insight path so the two never call the same factor two different things. Structured factors are
/// looked up; a text theme cannot be, because its key carries the user's own words.
/// </summary>
public static class FactorNames
{
    public static string Display(string key)
    {
        if (key.StartsWith(CorrelationService.Factors.ThemePrefix, StringComparison.Ordinal))
        {
            return string.Format(
                AppResources.FactorThemeFormat,
                key[CorrelationService.Factors.ThemePrefix.Length..]);
        }

        return key switch
        {
            CorrelationService.Factors.SleepDuration => AppResources.FactorSleepDuration,
            CorrelationService.Factors.SleepQuality => AppResources.FactorSleepQuality,
            CorrelationService.Factors.CyclePeriodDay => AppResources.FactorCyclePeriodDay,
            CorrelationService.Factors.CycleSymptomLoad => AppResources.FactorCycleSymptoms,
            CorrelationService.Factors.HabitCompletion => AppResources.FactorHabitCompletion,
            CorrelationService.Factors.MealsLogged => AppResources.FactorMealsLogged,
            CorrelationService.Factors.TaskCompletion => AppResources.FactorTaskCompletion,
            CorrelationService.Factors.DailySpend => AppResources.FactorDailySpend,
            _ => key
        };
    }

    /// <summary>The same name fit for the middle of a sentence («найближче — тривалість сну»).</summary>
    public static string DisplayLower(string key)
    {
        var name = Display(key);
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
