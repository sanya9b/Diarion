using System;
using System.Collections.Generic;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class DiaryEntryContentTests
{
    [Fact]
    public void HasContent_FreshEntry_IsFalse()
    {
        new DiaryEntry { Date = DateTime.Today }.HasContent().Should().BeFalse();
    }

    [Fact]
    public void HasContent_Null_IsFalse()
    {
        ((DiaryEntry?)null).HasContent().Should().BeFalse();
    }

    [Fact]
    public void HasContent_CycleDayOnly_IsFalse()
    {
        // Written by the day screen itself whenever cycle tracking is on — not something the user typed.
        new DiaryEntry { Date = DateTime.Today, CycleDay = "14" }.HasContent().Should().BeFalse();
    }

    [Fact]
    public void HasContent_WhitespaceOnlyText_IsFalse()
    {
        new DiaryEntry { Gratitude = "   ", Triggers = "\t" }.HasContent().Should().BeFalse();
    }

    [Fact]
    public void HasContent_UncompletedHabitsOnly_IsFalse()
    {
        var entry = new DiaryEntry
        {
            HabitsList = new List<HabitItem>
            {
                new() { Name = "Water", IsCompleted = false }
            }
        };

        // Habits are synced onto every entry on load, so an untouched list is not content.
        entry.HasContent().Should().BeFalse();
    }

    public static IEnumerable<object[]> ContentfulEntries() => new List<object[]>
    {
        new object[] { new DiaryEntry { Emotion = Emotion.Happy } },
        new object[] { new DiaryEntry { HourlyMood = { new HourMood { Hour = 9, Mood = Emotion.Calm } } } },
        new object[] { new DiaryEntry { Triggers = "deadline" } },
        new object[] { new DiaryEntry { Gratitude = "sun" } },
        new object[] { new DiaryEntry { SoulFood = "music" } },
        new object[] { new DiaryEntry { SupportForOthers = "helped a friend" } },
        new object[] { new DiaryEntry { Title = "title" } },
        new object[] { new DiaryEntry { Content = "body" } },
        new object[] { new DiaryEntry { SleepNotes = "restless" } },
        new object[] { new DiaryEntry { IntimateLife = "yes" } },
        new object[] { new DiaryEntry { IsIntimateLifeDone = true } },
        new object[] { new DiaryEntry { SleepStart = new TimeSpan(23, 0, 0) } },
        new object[] { new DiaryEntry { SleepEnd = new TimeSpan(7, 0, 0) } },
        new object[] { new DiaryEntry { SleepQuality = 6 } },
        new object[] { new DiaryEntry { HealthStatus = 7 } },
        new object[] { new DiaryEntry { IsBreakfastDone = true } },
        new object[] { new DiaryEntry { LunchFood = "soup" } },
        new object[] { new DiaryEntry { IsDinnerDone = true } },
        new object[] { new DiaryEntry { HabitsList = new List<HabitItem> { new() { IsCompleted = true } } } },
    };

    [Theory]
    [MemberData(nameof(ContentfulEntries))]
    public void HasContent_AnySingleUserSuppliedField_IsTrue(DiaryEntry entry)
    {
        entry.HasContent().Should().BeTrue();
    }
}
