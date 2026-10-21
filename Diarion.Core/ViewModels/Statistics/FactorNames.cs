using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

/// <summary>
/// The user-facing name of a correlation factor, so every screen calls a factor the same thing.
/// </summary>
public static class FactorNames
{
    public static string Display(string key)
    {
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
