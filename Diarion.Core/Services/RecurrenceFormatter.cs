using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>Human-readable, localized summary of a recurrence rule (e.g. "Daily" or "Mon, Wed, Fri").</summary>
public static class RecurrenceFormatter
{
    public static string Describe(RecurrenceRule? rule)
    {
        var r = rule ?? new RecurrenceRule();

        return r.Kind switch
        {
            RecurrenceKind.Weekly => DescribeWeekdays(r),
            RecurrenceKind.IntervalDays => string.Format(
                AppResources.RecurrenceEveryNDaysFormat, System.Math.Max(1, r.EveryN)),
            RecurrenceKind.MonthlyByDay => string.Format(
                AppResources.RecurrenceMonthlyOnDayFormat, System.Math.Clamp(r.DayOfMonth, 1, 31)),
            _ => AppResources.HabitScheduleDaily
        };
    }

    private static string DescribeWeekdays(RecurrenceRule rule)
    {
        // An empty day list never fires, but reading "Daily" here has been the behaviour since habits
        // shipped and the editor blocks saving one, so this stays a display quirk rather than a change.
        if (rule.DaysOfWeek == null || rule.DaysOfWeek.Count == 0)
        {
            return AppResources.HabitScheduleDaily;
        }

        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // indexed by (int)DayOfWeek
        var ordered = rule.DaysOfWeek
            .Distinct()
            .Where(d => d >= 0 && d < names.Length)
            .OrderBy(d => (d + 6) % 7); // Monday-first

        return string.Join(", ", ordered.Select(d => names[d]));
    }
}
