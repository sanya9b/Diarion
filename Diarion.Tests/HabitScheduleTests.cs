using System;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class HabitScheduleTests
{
    private static readonly DateTime Wednesday = new(2026, 7, 1);

    [Fact]
    public void Daily_IsScheduledEveryDay()
    {
        var s = new HabitSchedule { Type = HabitScheduleType.Daily };
        for (int i = 0; i < 7; i++)
        {
            s.IsScheduledOn(Wednesday.AddDays(i)).Should().BeTrue();
        }
    }

    [Fact]
    public void SpecificDays_OnlyListedWeekdays()
    {
        var s = new HabitSchedule
        {
            Type = HabitScheduleType.SpecificDays,
            DaysOfWeek = new() { (int)DayOfWeek.Monday, (int)DayOfWeek.Friday }
        };

        s.IsScheduledOn(new DateTime(2026, 6, 29)).Should().BeTrue();  // Monday
        s.IsScheduledOn(new DateTime(2026, 6, 26)).Should().BeTrue();  // Friday
        s.IsScheduledOn(Wednesday).Should().BeFalse();                 // Wednesday
    }

    [Fact]
    public void TimesPerWeek_IsScheduledEveryDay()
    {
        var s = new HabitSchedule { Type = HabitScheduleType.TimesPerWeek, TimesPerWeek = 3 };
        for (int i = 0; i < 7; i++)
        {
            s.IsScheduledOn(Wednesday.AddDays(i)).Should().BeTrue();
        }
    }
}
