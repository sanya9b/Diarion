using System;
using System.Collections.Generic;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>The parts of spec 20 that other code — Swift, an Android receiver — depends on byte for byte.</summary>
public class WidgetContractTests
{
    [Theory]
    [InlineData(null, false, WidgetPrivacyLevel.Counts)]
    [InlineData(null, true, WidgetPrivacyLevel.ButtonOnly)]
    [InlineData(WidgetPrivacyLevel.WithText, true, WidgetPrivacyLevel.WithText)]
    [InlineData(WidgetPrivacyLevel.ButtonOnly, false, WidgetPrivacyLevel.ButtonOnly)]
    public void Privacy_policy_defaults_follow_the_lock_and_a_choice_wins(
        WidgetPrivacyLevel? chosen, bool lockEnabled, WidgetPrivacyLevel expected)
    {
        WidgetPrivacyPolicy.Resolve(chosen, lockEnabled).Should().Be(expected);
    }

    [Fact]
    public void Json_uses_camel_case_string_enums_and_drops_null_sections()
    {
        var snapshot = new WidgetSnapshot
        {
            Date = "2026-11-05",
            GeneratedAt = new DateTimeOffset(2026, 11, 5, 14, 2, 0, TimeSpan.FromHours(2)),
            Locale = "uk",
            PrivacyLevel = WidgetPrivacyLevel.Counts,
            Labels = new WidgetLabels { AppName = "Diarion", Open = "Відкрити", DateShort = "ср, 5 лист.", Updated = "Оновлено 14:02" },
            Streak = new WidgetStreak { Days = 12, HeldByGrace = false, Label = "12 днів поспіль" },
            Mood = new WidgetMood { Quadrant = "PleasantCalm", Label = "Настрій дня" }
        };

        var json = WidgetSnapshotJson.Serialize(snapshot);

        json.Should().Contain("\"schemaVersion\":1");
        json.Should().Contain("\"privacyLevel\":\"Counts\"");
        json.Should().Contain("\"quadrant\":\"PleasantCalm\"");
        json.Should().Contain("\"heldByGrace\":false");
        json.Should().NotContain("\"tasks\"", "a section the level does not allow is absent, not null");
        json.Should().NotContain("\"habits\"");
        json.Should().NotContain("SchemaVersion", "the Swift decoder keys on camelCase");
    }

    [Fact]
    public void Json_round_trips_including_items()
    {
        var snapshot = new WidgetSnapshot
        {
            Date = "2026-11-05",
            GeneratedAt = DateTimeOffset.Now,
            Locale = "en",
            PrivacyLevel = WidgetPrivacyLevel.WithText,
            Labels = new WidgetLabels { AppName = "Diarion", Open = "Open diary", DateShort = "Wed, 5 Nov", Updated = "Updated 14:02" },
            Tasks = new WidgetTasks
            {
                Done = 1, Total = 2, Label = "1 of 2 tasks",
                Items = new List<WidgetTaskItem>
                {
                    new() { Title = "Dentist", Time = "14:00", Done = false, Priority = "High" },
                    new() { Title = "Call", Time = null, Done = true, Priority = "Medium" }
                }
            },
            Habits = new WidgetHabits
            {
                Done = 0, Total = 1, Label = "0 of 1 habits",
                Items = new List<WidgetHabitItem> { new() { Name = "Water", Done = false } }
            }
        };

        var back = WidgetSnapshotJson.Deserialize(WidgetSnapshotJson.Serialize(snapshot));

        back.Should().NotBeNull();
        back!.PrivacyLevel.Should().Be(WidgetPrivacyLevel.WithText);
        back.Tasks!.Items.Should().HaveCount(2);
        back.Tasks.Items![0].Title.Should().Be("Dentist");
        back.Tasks.Items[1].Time.Should().BeNull();
        back.Habits!.Items![0].Name.Should().Be("Water");
        back.Streak.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":1")]
    public void Json_reader_returns_null_for_garbage(string? json)
    {
        WidgetSnapshotJson.Deserialize(json).Should().BeNull();
    }

    [Theory]
    [InlineData("diarion://today", WidgetDestination.Today)]
    [InlineData("diarion://tasks", WidgetDestination.Tasks)]
    [InlineData("diarion://habits", WidgetDestination.Habits)]
    [InlineData("DIARION://Habits/", WidgetDestination.Habits)]
    [InlineData("  diarion://tasks  ", WidgetDestination.Tasks)]
    public void Deep_links_parse(string link, WidgetDestination expected)
    {
        WidgetDeepLink.TryParse(link, out var destination).Should().BeTrue();
        destination.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://diarion.org/today")]
    [InlineData("diarion://finance")]
    [InlineData("diarion:today")]
    [InlineData("today")]
    public void Deep_links_reject_what_is_not_ours(string? link)
    {
        WidgetDeepLink.TryParse(link, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(WidgetDestination.Today)]
    [InlineData(WidgetDestination.Tasks)]
    [InlineData(WidgetDestination.Habits)]
    public void Deep_links_round_trip(WidgetDestination destination)
    {
        WidgetDeepLink.TryParse(WidgetDeepLink.For(destination), out var back).Should().BeTrue();
        back.Should().Be(destination);
    }
}
