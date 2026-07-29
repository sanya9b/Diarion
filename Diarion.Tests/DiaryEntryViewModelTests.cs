using System;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class DiaryEntryViewModelTests
{
    [Fact]
    public void FoodTrackerProperties_ShouldUpdateModelCorrectly()
    {
        // Arrange
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);

        // Act & Assert Breakfast
        viewModel.IsBreakfastDone = true;
        viewModel.BreakfastFood = "Eggs";
        viewModel.IsBreakfastDone.Should().BeTrue();
        model.IsBreakfastDone.Should().BeTrue();
        model.BreakfastFood.Should().Be("Eggs");

        // Act & Assert Lunch
        viewModel.IsLunchDone = true;
        viewModel.LunchFood = "Salad";
        viewModel.IsLunchDone.Should().BeTrue();
        model.IsLunchDone.Should().BeTrue();
        model.LunchFood.Should().Be("Salad");

        // Act & Assert Dinner
        viewModel.IsDinnerDone = true;
        viewModel.DinnerFood = "Chicken";
        viewModel.IsDinnerDone.Should().BeTrue();
        model.IsDinnerDone.Should().BeTrue();
        model.DinnerFood.Should().Be("Chicken");
    }

    [Fact]
    public void SleepTrackerProperties_ShouldUpdateModelCorrectly()
    {
        // Arrange
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);

        var start = new TimeSpan(22, 0, 0);
        var end = new TimeSpan(6, 0, 0);

        // Act
        viewModel.SleepStart = start;
        viewModel.SleepEnd = end;
        viewModel.SleepQuality = 4;

        // Assert
        viewModel.HasSleepStart.Should().BeTrue();
        model.SleepStart.Should().Be(start);
        model.SleepEnd.Should().Be(end);
        model.SleepQuality.Should().Be(4);
    }

    [Fact]
    public void SelectEmotion_SetsEmotionAndPersistsToModelOnSync()
    {
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);

        viewModel.SelectEmotionCommand.Execute(Emotion.Sad);

        viewModel.Emotion.Should().Be(Emotion.Sad);
        viewModel.SyncToModel();
        model.Emotion.Should().Be(Emotion.Sad);
    }

    [Fact]
    public void PromptFields_RoundTripThroughTheModel()
    {
        var model = new DiaryEntry { PromptResourceKey = "PromptCbt03", PromptAnswer = "stored answer" };

        var viewModel = new DiaryEntryViewModel(model);
        viewModel.PromptResourceKey.Should().Be("PromptCbt03");
        viewModel.PromptAnswer.Should().Be("stored answer");

        viewModel.PromptResourceKey = "PromptOpen07";
        viewModel.PromptAnswer = "edited";
        viewModel.SyncToModel();

        model.PromptResourceKey.Should().Be("PromptOpen07");
        model.PromptAnswer.Should().Be("edited");
    }

    // --- Hourly mood ---

    [Fact]
    public void HourlyMood_MaterialisesEveryWakingHour_AndSeedsFromTheModel()
    {
        var model = new DiaryEntry
        {
            HourlyMood = { new HourMood { Hour = 14, Mood = Emotion.Sad } }
        };

        var viewModel = new DiaryEntryViewModel(model);

        viewModel.HourlyMood.Should().HaveCount(DiaryEntryViewModel.LastHour - DiaryEntryViewModel.FirstHour + 1);
        viewModel.HourlyMood.First().Hour.Should().Be(DiaryEntryViewModel.FirstHour);
        viewModel.HourlyMood.Last().Hour.Should().Be(DiaryEntryViewModel.LastHour);
        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.Sad);
        viewModel.HourlyMood.Single(h => h.Hour == 9).Mood.Should().Be(Emotion.None);
    }

    [Fact]
    public void SelectEmotion_WithNoHourSelected_SetsTheDayScalar()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        viewModel.SelectEmotionCommand.Execute(Emotion.Happy);

        viewModel.Emotion.Should().Be(Emotion.Happy);
        viewModel.HourlyMood.Should().OnlyContain(h => h.Mood == Emotion.None);
    }

    [Fact]
    public void SelectEmotion_WithAnHourSelected_WritesThatHourAndLeavesTheScalar()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Calm });

        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 14));
        viewModel.SelectEmotionCommand.Execute(Emotion.Sad);

        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.Sad);
        viewModel.Emotion.Should().Be(Emotion.Calm, "the day-level summary stays the user's own");
        viewModel.Model.HourlyMood.Should().ContainSingle(h => h.Hour == 14 && h.Mood == Emotion.Sad);
    }

    [Fact]
    public void SelectEmotion_SameEmotionTwiceOnAnHour_ClearsIt()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 9));

        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);
        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);

        viewModel.HourlyMood.Single(h => h.Hour == 9).Mood.Should().Be(Emotion.None);
        viewModel.Model.HourlyMood.Should().BeEmpty("cleared hours are not stored");
    }

    [Fact]
    public void SelectHour_TappingTheSameHourAgain_ReturnsToDayLevel()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        var slot = viewModel.HourlyMood.Single(h => h.Hour == 11);

        viewModel.SelectHourCommand.Execute(slot);
        viewModel.IsHourSelected.Should().BeTrue();

        viewModel.SelectHourCommand.Execute(slot);

        viewModel.IsHourSelected.Should().BeFalse();
        viewModel.HourlyMood.Should().OnlyContain(h => !h.IsSelected);
    }

    [Fact]
    public void CurrentMood_FollowsTheSelectedHour_ThenReturnsToTheDay()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Happy });
        var slot = viewModel.HourlyMood.Single(h => h.Hour == 20);

        viewModel.CurrentMood.Should().Be(Emotion.Happy);

        viewModel.SelectHourCommand.Execute(slot);
        viewModel.CurrentMood.Should().Be(Emotion.None, "that hour has nothing logged yet");

        viewModel.SelectEmotionCommand.Execute(Emotion.Anxious);
        viewModel.CurrentMood.Should().Be(Emotion.Anxious);

        viewModel.SelectHourCommand.Execute(slot);
        viewModel.CurrentMood.Should().Be(Emotion.Happy);
    }

    [Fact]
    public void SyncToModel_CopiesHourlyMood()
    {
        // The original defect: hourly mood was modelled and read, but SyncToModel never copied it,
        // so nothing the user could do would ever persist a value.
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);
        viewModel.HourlyMood.Single(h => h.Hour == 8).Mood = Emotion.Calm;

        viewModel.SyncToModel();

        model.HourlyMood.Should().ContainSingle(h => h.Hour == 8 && h.Mood == Emotion.Calm);
    }

    /// <summary>
    /// A stand-in for the seeded collection: ten prompts per category, all long-standing. Held as one
    /// instance per test — prompts are referenced by id, so a rebuilt library would resolve to nothing.
    /// Both languages carry the same text so assertions do not depend on the ambient culture.
    /// </summary>
    private readonly PromptLibrary _library = BuildLibrary();

    private static PromptLibrary BuildLibrary()
    {
        var prompts = new List<GuidedPrompt>();
        foreach (var (category, keys) in PromptCatalog.SeedKeys)
        {
            foreach (var key in keys)
            {
                prompts.Add(new GuidedPrompt
                {
                    ResourceKey = key,
                    Category = category,
                    TextEn = $"question {key}",
                    TextUk = $"question {key}",
                    CreatedAt = DateTime.MinValue
                });
            }
        }

        return new PromptLibrary(prompts);
    }

    private PromptCategory? CategoryOf(DiaryEntryViewModel vm) =>
        _library.Find(vm.PromptResourceKey)?.Category;

    [Fact]
    public void HourlyMood_DrivesThePromptCategory()
    {
        // PromptSelector has always preferred the hourly scale; until now that branch was unreachable.
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15), Emotion = Emotion.Happy }, _library);
        CategoryOf(viewModel).Should().NotBe(PromptCategory.CbtReframe);

        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 10));
        viewModel.SelectEmotionCommand.Execute(Emotion.Sad);

        CategoryOf(viewModel).Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void NewEntry_GetsAPromptImmediately()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) }, _library);

        viewModel.PromptResourceKey.Should().NotBeNullOrEmpty();
        viewModel.PromptText.Should().NotBeNullOrWhiteSpace();
        viewModel.HasPrompt.Should().BeTrue();
    }

    [Fact]
    public void WithoutALibrary_ThereIsNoPromptToShow()
    {
        // Constructible from a bare entry, e.g. in tests and any screen that has no prompt service.
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) });

        viewModel.PromptResourceKey.Should().BeEmpty();
        viewModel.HasPrompt.Should().BeFalse();
    }

    [Fact]
    public void LegacyResourceKeyOnAnEntry_StillResolves()
    {
        // Entries written before prompts moved into the database reference a built-in by resource key.
        var model = new DiaryEntry
        {
            Date = new DateTime(2026, 7, 15),
            Emotion = Emotion.Sad,
            PromptResourceKey = "PromptCbt03",
            PromptAnswer = "already answered"
        };

        var viewModel = new DiaryEntryViewModel(model, _library);

        viewModel.PromptResourceKey.Should().Be("PromptCbt03");
        viewModel.PromptText.Should().Be("question PromptCbt03");
    }

    [Fact]
    public void RecordingALowMood_SwitchesThePromptToReframing()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) }, _library);
        CategoryOf(viewModel).Should().Be(PromptCategory.OpenReflection);

        // Mood is normally recorded after the day screen has already opened.
        viewModel.Emotion = Emotion.Sad;

        CategoryOf(viewModel).Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void OnceAnswered_ThePromptStopsChanging()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) }, _library);
        viewModel.PromptAnswer = "half-written thought";
        var answering = viewModel.PromptResourceKey;

        viewModel.Emotion = Emotion.Sad;

        viewModel.PromptResourceKey.Should().Be(answering,
            "the question must not change under someone who is already answering it");
    }

    [Fact]
    public void StoredPromptSurvivesReload_WhenItStillSuitsTheMood()
    {
        var library = _library;
        var model = new DiaryEntry { Date = new DateTime(2026, 7, 15), Emotion = Emotion.Sad };
        var first = new DiaryEntryViewModel(model, library);
        first.ShufflePromptCommand.Execute(null);
        var shuffled = first.PromptResourceKey;
        first.SyncToModel();

        var reloaded = new DiaryEntryViewModel(model, library);

        reloaded.PromptResourceKey.Should().Be(shuffled);
    }

    [Fact]
    public void ShufflePrompt_MovesWithinTheSameCategory()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15), Emotion = Emotion.Sad }, _library);
        var before = viewModel.PromptResourceKey;

        viewModel.ShufflePromptCommand.Execute(null);

        viewModel.PromptResourceKey.Should().NotBe(before);
        CategoryOf(viewModel).Should().Be(PromptCategory.CbtReframe);
    }

    [Theory]
    [InlineData(Emotion.Happy, 2)]
    [InlineData(Emotion.Calm, 1)]
    [InlineData(Emotion.Anxious, -1)]
    [InlineData(Emotion.Sad, -2)]
    [InlineData(Emotion.Angry, -2)]
    [InlineData(Emotion.None, 0)]
    public void ToValence_MapsEmotionToScore(Emotion emotion, int expected)
    {
        emotion.ToValence().Should().Be(expected);
    }
}
