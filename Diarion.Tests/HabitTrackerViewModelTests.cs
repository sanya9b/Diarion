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
        var habitServiceMock = new Mock<IHabitService>();

        habitServiceMock
            .Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(() => storedTrackers.ToList());

        habitServiceMock
            .Setup(s => s.SaveHarmfulHabitTrackerAsync(It.IsAny<HarmfulHabitTracker>()))
            .Returns<HarmfulHabitTracker>(tracker =>
            {
                storedTrackers.RemoveAll(x => x.Id == tracker.Id);
                storedTrackers.Add(tracker);
                return Task.CompletedTask;
            });

        var dialogServiceMock = new Mock<IDialogService>();
        var viewModel = new HabitTrackerViewModel(
            habitServiceMock.Object,
            dialogServiceMock.Object,
            new Mock<INotificationService>().Object, TestProfiles.Service())
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
        viewModel.TrackerDays.Should().HaveCount(30);
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

        var habitServiceMock = new Mock<IHabitService>();
        habitServiceMock
            .Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        habitServiceMock
            .Setup(s => s.SetHarmfulHabitDayMarkedAsync(tracker.Id, It.IsAny<DateTime>(), true))
            .Returns(Task.CompletedTask);

        var dialogServiceMock = new Mock<IDialogService>();
        var viewModel = new HabitTrackerViewModel(
            habitServiceMock.Object,
            dialogServiceMock.Object,
            new Mock<INotificationService>().Object, TestProfiles.Service());
        await viewModel.LoadAsync();
        var day = viewModel.TrackerDays[0];

        // Act
        await viewModel.ToggleDayCommand.ExecuteAsync(day);

        // Assert
        day.IsMarked.Should().BeTrue();
        viewModel.SelectedTracker!.MarkedDaysCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_WithLongRunningTracker_CoversEveryElapsedDay()
    {
        var (viewModel, _) = await CreateWithTrackerAsync(DateTime.Today.AddDays(-44));

        var today = viewModel.TrackerDays.SingleOrDefault(d => d.Date == DateTime.Today);
        today.Should().NotBeNull("today must be markable without first marking the days before it");
        today!.DayNumber.Should().Be(45);
        viewModel.TrackerDays.Should().HaveCount(50);
    }

    [Fact]
    public async Task ToggleDayAsync_OnFutureDay_LeavesItFrozen()
    {
        var (viewModel, habitServiceMock) = await CreateWithTrackerAsync(DateTime.Today);

        var future = viewModel.TrackerDays.First(d => d.Date == DateTime.Today.AddDays(1));
        future.IsFuture.Should().BeTrue();

        await viewModel.ToggleDayCommand.ExecuteAsync(future);

        future.IsMarked.Should().BeFalse();
        viewModel.SelectedTracker!.MarkedDaysCount.Should().Be(0);
        habitServiceMock.Verify(
            s => s.SetHarmfulHabitDayMarkedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_WithTrackerStartedToday_StillDrawsFutureDays()
    {
        var (viewModel, _) = await CreateWithTrackerAsync(DateTime.Today);

        viewModel.TrackerDays.Should().HaveCount(30);
        viewModel.TrackerDays.Count(d => d.IsFuture).Should().Be(29);
    }

    private static async Task<(HabitTrackerViewModel ViewModel, Mock<IHabitService> HabitService)> CreateWithTrackerAsync(
        DateTime startDate)
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = startDate
        };

        var habitServiceMock = new Mock<IHabitService>();
        habitServiceMock
            .Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var viewModel = new HabitTrackerViewModel(
            habitServiceMock.Object,
            new Mock<IDialogService>().Object,
            new Mock<INotificationService>().Object, TestProfiles.Service());
        await viewModel.LoadAsync();
        return (viewModel, habitServiceMock);
    }
}

public class HabitTrackerReminderTests
{
    [Fact]
    public async Task AddTracker_WithReminderEnabled_PersistsTimeAndSchedulesDaily()
    {
        var (viewModel, habitService, notifications, saved) = CreateForAdd();
        viewModel.NewTrackerName = "Smoking";
        viewModel.NewTrackerReminderEnabled = true;
        viewModel.NewTrackerReminderTime = new TimeSpan(21, 30, 0);

        await viewModel.AddTrackerCommand.ExecuteAsync(null);

        saved.Single().ReminderTime.Should().Be(new TimeSpan(21, 30, 0));
        notifications.Verify(
            n => n.ScheduleHabitReminder(
                saved.Single().Id,
                "Smoking",
                new TimeSpan(21, 30, 0),
                null),
            Times.Once);
        notifications.Verify(n => n.RequestPermissionsAsync(), Times.Once);
        habitService.Verify(s => s.SaveHarmfulHabitTrackerAsync(It.IsAny<HarmfulHabitTracker>()), Times.Once);
    }

    [Fact]
    public async Task AddTracker_WithReminderOff_CancelsInsteadOfScheduling()
    {
        var (viewModel, _, notifications, saved) = CreateForAdd();
        viewModel.NewTrackerName = "Smoking";

        await viewModel.AddTrackerCommand.ExecuteAsync(null);

        saved.Single().ReminderTime.Should().BeNull();
        notifications.Verify(n => n.CancelHabitReminder(saved.Single().Id), Times.Once);
        notifications.Verify(
            n => n.ScheduleHabitReminder(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyList<int>?>()),
            Times.Never);
    }

    [Fact]
    public async Task EditTracker_LoadsStoredReminderIntoForm()
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-5),
            ReminderTime = new TimeSpan(7, 15, 0)
        };

        var habitService = new Mock<IHabitService>();
        habitService.Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var viewModel = new HabitTrackerViewModel(
            habitService.Object,
            new Mock<IDialogService>().Object,
            new Mock<INotificationService>().Object, TestProfiles.Service());
        await viewModel.LoadAsync();

        viewModel.EditTrackerCommand.Execute(viewModel.SelectedTracker);

        viewModel.NewTrackerReminderEnabled.Should().BeTrue();
        viewModel.NewTrackerReminderTime.Should().Be(new TimeSpan(7, 15, 0));
    }

    [Fact]
    public async Task DeleteTracker_CancelsItsReminder()
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-5),
            ReminderTime = new TimeSpan(7, 15, 0)
        };

        var habitService = new Mock<IHabitService>();
        habitService.Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var notifications = new Mock<INotificationService>();
        var viewModel = new HabitTrackerViewModel(habitService.Object, dialog.Object, notifications.Object, TestProfiles.Service());
        await viewModel.LoadAsync();

        await viewModel.DeleteTrackerCommand.ExecuteAsync(viewModel.SelectedTracker);

        notifications.Verify(n => n.CancelHabitReminder(tracker.Id), Times.Once);
    }

    private static (HabitTrackerViewModel ViewModel, Mock<IHabitService> HabitService, Mock<INotificationService> Notifications, List<HarmfulHabitTracker> Saved)
        CreateForAdd()
    {
        var saved = new List<HarmfulHabitTracker>();
        var habitService = new Mock<IHabitService>();

        habitService.Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(() => saved.ToList());

        habitService.Setup(s => s.SaveHarmfulHabitTrackerAsync(It.IsAny<HarmfulHabitTracker>()))
            .Returns<HarmfulHabitTracker>(t =>
            {
                saved.RemoveAll(x => x.Id == t.Id);
                saved.Add(t);
                return Task.CompletedTask;
            });

        var notifications = new Mock<INotificationService>();
        notifications.Setup(n => n.RequestPermissionsAsync()).ReturnsAsync(true);

        var viewModel = new HabitTrackerViewModel(
            habitService.Object,
            new Mock<IDialogService>().Object,
            notifications.Object, TestProfiles.Service());

        return (viewModel, habitService, notifications, saved);
    }
}