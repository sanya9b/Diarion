using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PromptSelectorTests
{
    private static readonly DateTime Day = new(2026, 7, 15);

    [Theory]
    [InlineData(Emotion.Sad, PromptCategory.CbtReframe)]
    [InlineData(Emotion.Angry, PromptCategory.CbtReframe)]
    [InlineData(Emotion.Anxious, PromptCategory.CbtReframe)]
    [InlineData(Emotion.None, PromptCategory.OpenReflection)]
    public void SelectCategory_MapsMoodToCategory(Emotion emotion, PromptCategory expected)
    {
        PromptSelector.SelectCategory(emotion, null, gratitudeWritten: false).Should().Be(expected);
    }

    [Fact]
    public void SelectCategory_GoodMoodWithoutGratitude_AsksForGratitude()
    {
        PromptSelector.SelectCategory(Emotion.Happy, null, gratitudeWritten: false)
            .Should().Be(PromptCategory.EveningGratitude);
    }

    [Fact]
    public void SelectCategory_GoodMoodWithGratitudeAlreadyWritten_Savours()
    {
        PromptSelector.SelectCategory(Emotion.Happy, null, gratitudeWritten: true)
            .Should().Be(PromptCategory.Savouring);
    }

    private static List<HourMood> Hours(params (int Hour, Emotion Mood)[] entries) =>
        entries.Select(e => new HourMood { Hour = e.Hour, Mood = e.Mood }).ToList();

    [Fact]
    public void SelectCategory_HourlyMoodOverridesTheDayLevelScalar()
    {
        var hourly = Hours((9, Emotion.Sad), (14, Emotion.Sad));

        // The scalar says the day was great; the hours say otherwise and must win.
        PromptSelector.SelectCategory(Emotion.Happy, hourly, gratitudeWritten: false)
            .Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void SelectCategory_NoHourlyMood_FallsBackToTheScalar()
    {
        PromptSelector.SelectCategory(Emotion.Sad, new List<HourMood>(), gratitudeWritten: false)
            .Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void SelectCategory_MixedHourlyMoodAveragesOut()
    {
        var hourly = Hours((9, Emotion.Happy), (14, Emotion.Sad));

        // +2 and -2 average to 0 — neither celebration nor reframing fits.
        PromptSelector.SelectCategory(Emotion.None, hourly, gratitudeWritten: false)
            .Should().Be(PromptCategory.OpenReflection);
    }

    [Fact]
    public void SelectKey_IsStableForTheSameDay()
    {
        var keys = Enumerable.Range(0, 100)
            .Select(_ => PromptSelector.SelectKey(Day, Emotion.Sad, null, false))
            .Distinct();

        keys.Should().ContainSingle("the question must not reshuffle every time the screen rebuilds");
    }

    [Fact]
    public void SelectKey_ReturnsAKeyFromTheChosenCategory()
    {
        var key = PromptSelector.SelectKey(Day, Emotion.Sad, null, false);

        PromptCatalog.KeysFor(PromptCategory.CbtReframe).Should().Contain(key);
    }

    [Fact]
    public void SelectKey_DiffersAcrossConsecutiveDays()
    {
        var first = PromptSelector.SelectKey(Day, Emotion.None, null, false);
        var second = PromptSelector.SelectKey(Day.AddDays(1), Emotion.None, null, false);

        second.Should().NotBe(first);
    }

    [Fact]
    public void SelectKey_CoversEveryPromptInACategoryAcrossAYear()
    {
        var seen = Enumerable.Range(0, 365)
            .Select(i => PromptSelector.SelectKey(Day.AddDays(i), Emotion.None, null, false))
            .Distinct();

        // A date-seeded index must not collapse onto a handful of prompts.
        seen.Should().BeEquivalentTo(PromptCatalog.KeysFor(PromptCategory.OpenReflection));
    }
}
