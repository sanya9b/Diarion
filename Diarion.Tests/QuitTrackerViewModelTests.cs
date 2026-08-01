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

        var vm = new HabitTrackerViewModel(
            habit.Object,
            new Mock<IDialogService>().Object,
            new Mock<INotificationService>().Object);
        await vm.LoadAsync();

        vm.EditTrackerCommand.Execute(vm.SelectedTracker);
        vm.NewTrackerCost = "2";
        vm.NewTrackerUnits = "20";
        await vm.AddTrackerCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.CostPerUnit.Should().Be(2m);
        saved.UnitsPerDay.Should().Be(20);
        saved.Relapses.Should().ContainSingle(); // the dormant relapse log survives an edit
    }

    [Fact]
    public async Task MoneySaved_CountsFromLatestRelapse()
    {
        var tracker = new HarmfulHabitTracker
        {
            Id = Guid.NewGuid(),
            HarmfulHabitName = "Smoking",
            StartDate = DateTime.Today.AddDays(-10),
            CostPerUnit = 2m,
            UnitsPerDay = 10,
            Relapses = new List<RelapseEvent> { new() { Date = DateTime.Today.AddDays(-3) } }
        };

        var habit = new Mock<IHabitService>();
        habit.Setup(s => s.GetHarmfulHabitTrackersAsync()).ReturnsAsync(new List<HarmfulHabitTracker> { tracker });

        var vm = new HabitTrackerViewModel(
            habit.Object,
            new Mock<IDialogService>().Object,
            new Mock<INotificationService>().Object);
        await vm.LoadAsync();

        // 3 clean days since the relapse × 10 units × 2 — the relapse still resets the maths
        vm.SelectedTracker!.HasMoney.Should().BeTrue();
        vm.SelectedTracker.MoneySavedText.Should().Be(60m.ToString("N2"));
    }
}
