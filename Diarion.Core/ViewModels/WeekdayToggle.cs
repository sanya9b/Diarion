using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.ViewModels;

/// <summary>
/// One weekday chip in a recurrence editor. Shared rather than restated per editor: which integer means
/// which day, and which day a week starts on, have one right answer, and the second place to state it is
/// the place it gets stated differently.
/// </summary>
public partial class WeekdayToggle : ObservableObject
{
    public int DayOfWeek { get; set; } // (int)System.DayOfWeek, 0 = Sunday … 6 = Saturday
    public string ShortName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Monday first, named by the current culture — matching how the calendar reads elsewhere.</summary>
    public static List<WeekdayToggle> BuildMondayFirst()
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // indexed by (int)DayOfWeek
        var toggles = new List<WeekdayToggle>();
        foreach (var dow in new[] { 1, 2, 3, 4, 5, 6, 0 })
        {
            toggles.Add(new WeekdayToggle { DayOfWeek = dow, ShortName = names[dow] });
        }
        return toggles;
    }
}
