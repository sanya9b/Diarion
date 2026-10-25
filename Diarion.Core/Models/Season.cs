using System;

namespace Diarion.Models;

/// <summary>
/// The season of a date, for the picture on the streak tile: a sprout in spring, a sun in summer, a yellow
/// leaf in autumn, a snowflake in winter (owner, 2026-10-23). Meteorological seasons — whole months,
/// December to February is winter — because that is how the seasons are spoken of where the app is made,
/// and an astronomical boundary on the 21st would turn the tile mid-week for no reason anyone sees.
/// Northern hemisphere; a southern setting would be a new member here, not a different calendar.
/// </summary>
public enum Season
{
    Winter,
    Spring,
    Summer,
    Autumn
}

public static class SeasonCalendar
{
    public static Season Of(DateTime date) => date.Month switch
    {
        12 or 1 or 2 => Season.Winter,
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        _ => Season.Autumn
    };
}
