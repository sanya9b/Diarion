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

    [Fact]
    public void NewEntry_GetsAPromptImmediately()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) });

        viewModel.PromptResourceKey.Should().NotBeNullOrEmpty();
        viewModel.PromptText.Should().NotBe(viewModel.PromptResourceKey, "the key must resolve to real text");
    }

    [Fact]
    public void RecordingALowMood_SwitchesThePromptToReframing()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) });
        PromptCatalog.CategoryOf(viewModel.PromptResourceKey).Should().Be(PromptCategory.OpenReflection);

        // Mood is normally recorded after the day screen has already opened.
        viewModel.Emotion = Emotion.Sad;

        PromptCatalog.CategoryOf(viewModel.PromptResourceKey).Should().Be(PromptCategory.CbtReframe);
    }

    [Fact]
    public void OnceAnswered_ThePromptStopsChanging()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15) });
        viewModel.PromptAnswer = "half-written thought";
        var answering = viewModel.PromptResourceKey;

        viewModel.Emotion = Emotion.Sad;

        viewModel.PromptResourceKey.Should().Be(answering,
            "the question must not change under someone who is already answering it");
    }

    [Fact]
    public void StoredPromptSurvivesReload_WhenItStillSuitsTheMood()
    {
        var model = new DiaryEntry { Date = new DateTime(2026, 7, 15), Emotion = Emotion.Sad };
        var first = new DiaryEntryViewModel(model);
        first.ShufflePromptCommand.Execute(null);
        var shuffled = first.PromptResourceKey;
        first.SyncToModel();

        var reloaded = new DiaryEntryViewModel(model);

        reloaded.PromptResourceKey.Should().Be(shuffled);
    }

    [Fact]
    public void ShufflePrompt_MovesWithinTheSameCategory()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 7, 15), Emotion = Emotion.Sad });
        var before = viewModel.PromptResourceKey;

        viewModel.ShufflePromptCommand.Execute(null);

        viewModel.PromptResourceKey.Should().NotBe(before);
        PromptCatalog.CategoryOf(viewModel.PromptResourceKey).Should().Be(PromptCategory.CbtReframe);
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
