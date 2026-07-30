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

public class HabitEditorViewModelTests
{
    private readonly Mock<IHabitService> _habitService = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly Mock<IDialogService> _dialog = new();
    private readonly Mock<INotificationService> _notification = new();

    private HabitEditorViewModel CreateVm() => new(_habitService.Object, _navigation.Object, _dialog.Object, _notification.Object);

    [Fact]
    public async Task Save_NewHabitWithSpecificDays_AddsWithSchedule_AndNavigatesBack()
    {
        var vm = CreateVm();
        vm.Name = "Gym";
        vm.SetSpecificDaysCommand.Execute(null);
        vm.Weekdays.First(w => w.DayOfWeek == 1).IsSelected = true; // Monday
        vm.Weekdays.First(w => w.DayOfWeek == 3).IsSelected = true; // Wednesday

        await vm.SaveCommand.ExecuteAsync(null);

        _habitService.Verify(s => s.AddHabitDefinitionAsync(It.Is<HabitDefinition>(h =>
            h.Name == "Gym" &&
            h.Schedule.Kind == RecurrenceKind.Weekly &&
            h.Target == null &&
            h.Schedule.DaysOfWeek.Contains(1) &&
            h.Schedule.DaysOfWeek.Contains(3))), Times.Once);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Save_SpecificDaysWithNoDaysSelected_ShowsValidation_AndDoesNotSave()
    {
        var vm = CreateVm();
        vm.Name = "Gym";
        vm.SetSpecificDaysCommand.Execute(null); // specific days but none selected

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasValidationMessage.Should().BeTrue();
        _habitService.Verify(s => s.AddHabitDefinitionAsync(It.IsAny<HabitDefinition>()), Times.Never);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Never);
    }

    [Fact]
    public async Task Save_TimesPerWeek_AddsWithWeeklyTarget()
    {
        var vm = CreateVm();
        vm.Name = "Run";
        vm.SetTimesPerWeekCommand.Execute(null);
        vm.IncrementTimesCommand.Execute(null); // default 3 -> 4

        await vm.SaveCommand.ExecuteAsync(null);

        // A quota habit is open on every day; the "how many" lives on the target, not the schedule.
        _habitService.Verify(s => s.AddHabitDefinitionAsync(It.Is<HabitDefinition>(h =>
            h.Schedule.Kind == RecurrenceKind.Daily &&
            h.Target != null &&
            h.Target.TimesPerWeek == 4)), Times.Once);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Save_WithReminderEnabled_SchedulesReminder()
    {
        var vm = CreateVm();
        vm.Name = "Water";
        vm.ReminderEnabled = true;
        vm.ReminderTime = new TimeSpan(8, 30, 0);

        await vm.SaveCommand.ExecuteAsync(null);

        _notification.Verify(n => n.ScheduleHabitReminder(
            It.IsAny<Guid>(), "Water", new TimeSpan(8, 30, 0), It.IsAny<IReadOnlyList<int>>()), Times.Once);
    }

    [Fact]
    public async Task Save_WithReminderDisabled_CancelsReminder()
    {
        var vm = CreateVm();
        vm.Name = "Water";
        vm.ReminderEnabled = false;

        await vm.SaveCommand.ExecuteAsync(null);

        _notification.Verify(n => n.CancelHabitReminder(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task Save_EmptyName_ShowsValidation_AndDoesNotSave()
    {
        var vm = CreateVm();
        vm.Name = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasValidationMessage.Should().BeTrue();
        _habitService.Verify(s => s.AddHabitDefinitionAsync(It.IsAny<HabitDefinition>()), Times.Never);
    }
}
