using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class QuitTrackerViewModelTests
{
    [Fact]
    public async Task Relapse_Confirmed_ResetsCleanDays_AndPersists()
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-10)
        };

        var habit = new Mock<IHabitService>();
        habit.Setup(s => s.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var vm = new HabitTrackerViewModel(habit.Object, dialog.Object);
        await vm.LoadAsync();

        var item = vm.SelectedTracker!;
        item.CleanDaysText.Should().Be("10"); // clean since start, no relapses yet

        await vm.RelapseCommand.ExecuteAsync(item);

        habit.Verify(s => s.AddRelapseAsync(tracker.Id, It.IsAny<DateTime>(), It.IsAny<string>()), Times.Once);
        item.CleanDaysText.Should().Be("0");        // relapse today resets the clean streak
        item.RelapseCountText.Should().Be("1");
        item.HasRelapses.Should().BeTrue();
    }

    [Fact]
    public async Task Edit_SetsCostAndUnits_PreservingRelapses()
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-10),
            Relapses = new List<RelapseEvent> { new() { Date = DateTime.Today.AddDays(-2) } }
        };

        var habit = new Mock<IHabitService>();
        habit.Setup(s => s.GetHarmfulHabitTrackersAsync()).ReturnsAsync(new List<HarmfulHabitTracker> { tracker });
        habit.Setup(s => s.GetHarmfulHabitTrackerByIdAsync(tracker.Id)).ReturnsAsync(tracker);

        HarmfulHabitTracker? saved = null;
        habit.Setup(s => s.SaveHarmfulHabitTrackerAsync(It.IsAny<HarmfulHabitTracker>()))
            .Returns<HarmfulHabitTracker>(t => { saved = t; return Task.CompletedTask; });

        var vm = new HabitTrackerViewModel(habit.Object, new Mock<IDialogService>().Object);
        await vm.LoadAsync();

        vm.EditTrackerCommand.Execute(vm.SelectedTracker);
        vm.NewTrackerCost = "2";
        vm.NewTrackerUnits = "20";
        await vm.AddTrackerCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.CostPerUnit.Should().Be(2m);
        saved.UnitsPerDay.Should().Be(20);
        saved.Relapses.Should().ContainSingle(); // preserved through edit
    }

    [Fact]
    public async Task Relapse_Declined_DoesNothing()
    {
        var tracker = new HarmfulHabitTracker { Id = Guid.NewGuid(), HarmfulHabitName = "Smoking", StartDate = DateTime.Today.AddDays(-3) };

        var habit = new Mock<IHabitService>();
        habit.Setup(s => s.GetHarmfulHabitTrackersAsync()).ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var dialog = new Mock<IDialogService>();
        dialog.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var vm = new HabitTrackerViewModel(habit.Object, dialog.Object);
        await vm.LoadAsync();

        await vm.RelapseCommand.ExecuteAsync(vm.SelectedTracker);

        habit.Verify(s => s.AddRelapseAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
        vm.SelectedTracker!.CleanDaysText.Should().Be("3");
    }
}
