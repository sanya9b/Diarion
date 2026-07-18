using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>Human-readable, localized summary of a habit schedule (e.g. "Daily" or "Mon, Wed, Fri").</summary>
public static class HabitScheduleFormatter
{
    public static string Describe(HabitSchedule? schedule)
    {
        var s = schedule ?? new HabitSchedule();
        if (s.Type == HabitScheduleType.Daily || s.DaysOfWeek == null || s.DaysOfWeek.Count == 0)
        {
            return AppResources.HabitScheduleDaily;
        }

        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // indexed by (int)DayOfWeek
        var ordered = s.DaysOfWeek
            .Distinct()
            .Where(d => d >= 0 && d < names.Length)
            .OrderBy(d => (d + 6) % 7); // Monday-first

        return string.Join(", ", ordered.Select(d => names[d]));
    }
}
