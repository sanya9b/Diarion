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

    private HabitEditorViewModel CreateVm() => new(_habitService.Object, _navigation.Object, _dialog.Object);

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
            h.Schedule.Type == HabitScheduleType.SpecificDays &&
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

        _habitService.Verify(s => s.AddHabitDefinitionAsync(It.Is<HabitDefinition>(h =>
            h.Schedule.Type == HabitScheduleType.TimesPerWeek &&
            h.Schedule.TimesPerWeek == 4)), Times.Once);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
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
