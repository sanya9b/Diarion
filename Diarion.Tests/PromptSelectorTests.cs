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

    /// <summary>A pool of <paramref name="perCategory"/> prompts in every category, created long ago.</summary>
    private static PromptLibrary Pool(int perCategory)
    {
        var prompts = new List<GuidedPrompt>();
        foreach (var category in PromptCatalog.SeedKeys.Keys)
        {
            for (int i = 0; i < perCategory; i++)
            {
                prompts.Add(new GuidedPrompt
                {
                    Category = category,
                    TextEn = $"{category}-{i}",
                    CreatedAt = new DateTime(2020, 1, 1).AddDays(i)
                });
            }
        }

        return new PromptLibrary(prompts);
    }

    [Fact]
    public void Select_IsStableForTheSameDay()
    {
        var library = Pool(10);

        var picked = Enumerable.Range(0, 100)
            .Select(_ => PromptSelector.Select(Day, Emotion.Sad, null, false, library)?.Id)
            .Distinct();

        picked.Should().ContainSingle("the question must not reshuffle every time the screen rebuilds");
    }

    [Fact]
    public void Select_ReturnsAPromptFromTheChosenCategory()
    {
        var picked = PromptSelector.Select(Day, Emotion.Sad, null, false, Pool(10));

        picked!.Category.Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void Select_DiffersAcrossConsecutiveDays()
    {
        var library = Pool(10);

        var first = PromptSelector.Select(Day, Emotion.None, null, false, library);
        var second = PromptSelector.Select(Day.AddDays(1), Emotion.None, null, false, library);

        second!.Id.Should().NotBe(first!.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(13)]
    public void Select_CoversEveryPromptInACategoryAcrossAYear(int perCategory)
    {
        var library = Pool(perCategory);

        var seen = Enumerable.Range(0, 365)
            .Select(i => PromptSelector.Select(Day.AddDays(i), Emotion.None, null, false, library)!.Id)
            .Distinct();

        // A date-seeded index must not collapse onto a handful of prompts, whatever the pool size.
        seen.Should().HaveCount(perCategory);
    }

    [Fact]
    public void Select_EmptyCategory_ReturnsNull()
    {
        var library = new PromptLibrary(new[]
        {
            new GuidedPrompt { Category = PromptCategory.Savouring, TextEn = "only savouring" }
        });

        PromptSelector.Select(Day, Emotion.Sad, null, false, library).Should().BeNull();
    }

    [Fact]
    public void Select_IgnoresPromptsCreatedAfterTheDay()
    {
        var library = new PromptLibrary(new[]
        {
            new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "old", CreatedAt = Day.AddDays(-1) },
            new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "new", CreatedAt = Day.AddDays(1) }
        });

        // A prompt written tomorrow cannot be the question a past day asked.
        PromptSelector.Select(Day, Emotion.Sad, null, false, library)!.TextEn.Should().Be("old");
    }

    [Fact]
    public void Select_IgnoresDeletedPrompts()
    {
        var library = new PromptLibrary(new[]
        {
            new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "kept", CreatedAt = new DateTime(2020, 1, 1) },
            new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "gone", CreatedAt = new DateTime(2020, 1, 2), DeletedAt = Day }
        });

        Enumerable.Range(0, 30)
            .Select(i => PromptSelector.Select(Day.AddDays(i), Emotion.Sad, null, false, library)!.TextEn)
            .Should().OnlyContain(text => text == "kept");
    }

    [Fact]
    public void Select_IsUnaffectedByTheOrderTheLibraryWasBuiltIn()
    {
        var a = new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "a", CreatedAt = new DateTime(2020, 1, 1) };
        var b = new GuidedPrompt { Category = PromptCategory.CbtReframe, TextEn = "b", CreatedAt = new DateTime(2020, 1, 2) };

        var forwards = PromptSelector.Select(Day, Emotion.Sad, null, false, new PromptLibrary(new[] { a, b }));
        var backwards = PromptSelector.Select(Day, Emotion.Sad, null, false, new PromptLibrary(new[] { b, a }));

        backwards!.Id.Should().Be(forwards!.Id);
    }

    [Fact]
    public void Next_WrapsWithinTheCategory()
    {
        var library = Pool(3);
        var start = PromptSelector.Select(Day, Emotion.Sad, null, false, library)!;

        var seen = new List<Guid>();
        var cursor = start;
        for (int i = 0; i < 3; i++)
        {
            cursor = PromptSelector.Next(cursor, Day, library)!;
            seen.Add(cursor.Id);
        }

        seen.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        cursor.Id.Should().Be(start.Id, "three steps through three prompts returns to the start");
    }
}
