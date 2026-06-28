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
}
