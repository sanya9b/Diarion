using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class HabitTrackerViewModelTests
{
    [Fact]
    public async Task AddTrackerAsync_WithValidInput_CreatesAndSelectsTracker()
    {
        // Arrange
        var storedTrackers = new List<HarmfulHabitTracker>();
        var diaryServiceMock = new Mock<IDiaryService>();

        diaryServiceMock
            .Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(() => storedTrackers.ToList());

        diaryServiceMock
            .Setup(s => s.SaveHarmfulHabitTrackerAsync(It.IsAny<HarmfulHabitTracker>()))
            .Returns<HarmfulHabitTracker>(tracker =>
            {
                storedTrackers.RemoveAll(x => x.Id == tracker.Id);
                storedTrackers.Add(tracker);
                return Task.CompletedTask;
            });

        var viewModel = new HabitTrackerViewModel(diaryServiceMock.Object)
        {
            NewTrackerName = "Smoking",
            NewTrackerStartDate = DateTime.Today.AddDays(-3)
        };

        // Act
        await viewModel.AddTrackerCommand.ExecuteAsync(null);

        // Assert
        storedTrackers.Should().ContainSingle();
        viewModel.SelectedTracker.Should().NotBeNull();
        viewModel.SelectedTracker!.HarmfulHabitName.Should().Be("Smoking");
        viewModel.TrackerDays.Should().HaveCount(4);
        viewModel.ValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleDayAsync_WhenTrackerSelected_UpdatesMarkedStateAndCount()
    {
        // Arrange
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-2)
        };

        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        diaryServiceMock
            .Setup(s => s.SetHarmfulHabitDayMarkedAsync(tracker.Id, It.IsAny<DateTime>(), true))
            .Returns(Task.CompletedTask);

        var viewModel = new HabitTrackerViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        var day = viewModel.TrackerDays[0];

        // Act
        await viewModel.ToggleDayCommand.ExecuteAsync(day);

        // Assert
        day.IsMarked.Should().BeTrue();
        viewModel.SelectedTracker!.MarkedDaysCount.Should().Be(1);
    }
}