using Diarion.Models;
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
