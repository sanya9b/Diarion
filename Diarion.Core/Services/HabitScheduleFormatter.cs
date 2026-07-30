using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>Human-readable, localized summary of a habit's schedule (e.g. "Daily" or "Mon, Wed, Fri").</summary>
public static class HabitScheduleFormatter
{
    public static string Describe(RecurrenceRule? schedule, CompletionTarget? target = null)
    {
        // The weekly quota is habit-only, so it stays here rather than in the shared formatter.
        if (target != null)
        {
            return string.Format(AppResources.HabitScheduleTimesPerWeekFormat, System.Math.Max(1, target.TimesPerWeek));
        }

        return RecurrenceFormatter.Describe(schedule);
    }
}
