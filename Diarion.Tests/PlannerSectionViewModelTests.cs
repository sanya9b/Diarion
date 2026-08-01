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

public class PlannerSectionViewModelTests
{
    private static readonly DateTime Day = new(2026, 7, 30);

    private static TodoItem Task(string text, int? hour = null, int minute = 0) => new()
    {
        TargetDate = Day,
        TaskDescription = text,
        HasTime = hour != null,
        TargetTime = hour == null ? TimeSpan.Zero : new TimeSpan(hour.Value, minute, 0)
    };

    /// <summary>A live daily rule for every series any of these rows belongs to.</summary>
    private static List<RecurringTask> LiveRulesFor(IEnumerable<TodoItem> todos)
        => todos.Where(t => t.RecurringTaskId != null)
                .Select(t => t.RecurringTaskId!.Value)
                .Distinct()
                .Select(id => new RecurringTask { Id = id })
                .ToList();

    private static PlannerSectionViewModel NewViewModel(params TodoItem[] todos)
    {
        var service = new Mock<ITodoService>();
        service.Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>())).ReturnsAsync(todos.ToList());
        service.Setup(s => s.GetRecurringTasksAsync()).ReturnsAsync(LiveRulesFor(todos));
        return new PlannerSectionViewModel(
            service.Object, new Mock<INavigationService>().Object, new Mock<IDialogService>().Object);
    }

    private static (PlannerSectionViewModel Vm, Mock<ITodoService> Service, Mock<IDialogService> Dialog)
        NewViewModelWithDialog(params TodoItem[] todos)
    {
        var service = new Mock<ITodoService>();
        service.Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>())).ReturnsAsync(todos.ToList());
        service.Setup(s => s.GetRecurringTasksAsync()).ReturnsAsync(LiveRulesFor(todos));
        var dialog = new Mock<IDialogService>();
        return (new PlannerSectionViewModel(service.Object, new Mock<INavigationService>().Object, dialog.Object),
                service, dialog);
    }

    private static TodoItem Occurrence(string text, Guid ruleId, int hour = 9)
    {
        var todo = Task(text, hour);
        todo.RecurringTaskId = ruleId;
        return todo;
    }

    [Fact]
    public async Task DeletingAOneOffAsksNothing()
    {
        var (viewModel, service, dialog) = NewViewModelWithDialog(Task("Купити хліб", 9));
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.DeleteTodoCommand.ExecuteAsync(viewModel.Todos.Single());

        dialog.Verify(d => d.ShowActionSheetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
        service.Verify(s => s.DeleteTodoAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task DeletingAnOccurrenceAsksWhichOfTheTwoThingsWasMeant()
    {
        var ruleId = Guid.NewGuid();
        var (viewModel, service, dialog) = NewViewModelWithDialog(Occurrence("Стретчинг", ruleId));
        dialog.Setup(d => d.ShowActionSheetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()))
              .ReturnsAsync(Diarion.Resources.Localization.AppResources.DeleteThisOccurrenceOption);
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.DeleteTodoCommand.ExecuteAsync(viewModel.Todos.Single());

        service.Verify(s => s.DeleteTodoAsync(It.IsAny<Guid>()), Times.Once);
        service.Verify(s => s.DeleteRecurringTaskAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ChoosingTheWholeSeriesDeletesTheRuleRatherThanTheRow()
    {
        var ruleId = Guid.NewGuid();
        var (viewModel, service, dialog) = NewViewModelWithDialog(Occurrence("Стретчинг", ruleId));
        dialog.Setup(d => d.ShowActionSheetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()))
              .ReturnsAsync(Diarion.Resources.Localization.AppResources.DeleteWholeSeriesOption);
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.DeleteTodoCommand.ExecuteAsync(viewModel.Todos.Single());

        service.Verify(s => s.DeleteRecurringTaskAsync(ruleId), Times.Once);
        service.Verify(s => s.DeleteTodoAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task BackingOutOfTheDialogDeletesNothingAtAll()
    {
        // The reason the sheet was worth a new dialog method: a two-button confirmation has no way to say
        // "neither", and this is a destructive action with two destructive answers.
        var (viewModel, service, dialog) = NewViewModelWithDialog(Occurrence("Стретчинг", Guid.NewGuid()));
        dialog.Setup(d => d.ShowActionSheetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()))
              .ReturnsAsync((string?)null);
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.DeleteTodoCommand.ExecuteAsync(viewModel.Todos.Single());

        service.Verify(s => s.DeleteTodoAsync(It.IsAny<Guid>()), Times.Never);
        service.Verify(s => s.DeleteRecurringTaskAsync(It.IsAny<Guid>()), Times.Never);
        viewModel.Todos.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadTodosForDate_MarksARowThatBelongsToASeries()
    {
        // The row needs to say so on the list: without it a repeating task and a one-off are
        // indistinguishable until the form is reopened.
        var oneOff = Task("Купити хліб", 9);
        var occurrence = Task("Стретчинг", 10);
        occurrence.RecurringTaskId = Guid.NewGuid();
        var viewModel = NewViewModel(oneOff, occurrence);

        await viewModel.LoadTodosForDateAsync(Day);

        var rows = viewModel.HourSlots.SelectMany(s => s.Items).ToList();
        rows.Single(t => t.TaskDescription == "Стретчинг").IsRecurring.Should().BeTrue();
        rows.Single(t => t.TaskDescription == "Купити хліб").IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task LoadTodosForDate_ARowWhoseSeriesHasEndedNoLongerSaysItRepeats()
    {
        // Switching the repeat off ends the series but leaves the occurrence pointing at its rule, because
        // that provenance is what pins it against auto-migration. Reading the glyph off the id alone meant
        // the row kept claiming to repeat — which looks exactly like the switch not having worked.
        var ruleId = Guid.NewGuid();
        var ended = Occurrence("Стретчинг", ruleId);
        var service = new Mock<ITodoService>();
        service.Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>())).ReturnsAsync(new List<TodoItem> { ended });
        service.Setup(s => s.GetRecurringTasksAsync()).ReturnsAsync(new List<RecurringTask>
        {
            new() { Id = ruleId, Recurrence = new RecurrenceRule { EndDate = Day.AddDays(-1) } }
        });
        var viewModel = new PlannerSectionViewModel(
            service.Object, new Mock<INavigationService>().Object, new Mock<IDialogService>().Object);

        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.Todos.Single().IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task LoadTodosForDate_BuildsOneRowPerHourFromFiveToTwentyThree()
    {
        var viewModel = NewViewModel();

        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.HourSlots.Should().HaveCount(19);
        viewModel.HourSlots.First().Hour.Should().Be(5);
        viewModel.HourSlots.Last().Hour.Should().Be(23);
        viewModel.HourSlots.Should().OnlyContain(s => s.IsEmpty);
    }

    [Fact]
    public async Task LoadTodosForDate_PutsTimedTasksInTheirHourAndUntimedInTheTray()
    {
        var viewModel = NewViewModel(Task("Gym", 8), Task("Milk"), Task("Meeting", 10));

        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.HourSlots.Single(s => s.Hour == 8).Items.Single().TaskDescription.Should().Be("Gym");
        viewModel.HourSlots.Single(s => s.Hour == 10).Items.Single().TaskDescription.Should().Be("Meeting");
        viewModel.UntimedTodos.Single().TaskDescription.Should().Be("Milk");
        viewModel.HasUntimedTodos.Should().BeTrue();
    }

    [Fact]
    public async Task LoadTodosForDate_MinutesLandInTheirHourRow()
    {
        var viewModel = NewViewModel(Task("Standup", 8, 30));

        await viewModel.LoadTodosForDateAsync(Day);

        // The row is the hour; the block prints the real time so 08:30 is not read as 08:00.
        viewModel.HourSlots.Single(s => s.Hour == 8).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadTodosForDate_SeveralTasksInOneHour_AllStayInThatRow()
    {
        var viewModel = NewViewModel(Task("A", 9), Task("B", 9, 45), Task("C", 9, 15));

        await viewModel.LoadTodosForDateAsync(Day);

        var slot = viewModel.HourSlots.Single(s => s.Hour == 9);
        slot.Items.Should().HaveCount(3);
        slot.IsEmpty.Should().BeFalse();
        // Ordered by time, so the row reads down the clock.
        slot.Items.Select(i => i.TaskDescription).Should().Equal("A", "C", "B");
    }

    [Theory]
    [InlineData(0, 5)]    // small hours clamp up to the first row
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(23, 23)]
    public async Task LoadTodosForDate_TasksOutsideTheWindowLandOnTheNearestEdgeRow(int hour, int expectedSlot)
    {
        // Hidden is the one thing they must not be: a task nobody can see is a task nobody does.
        var viewModel = NewViewModel(Task("Edge", hour));

        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.HourSlots.Single(s => s.Hour == expectedSlot).Items.Should().ContainSingle();
        viewModel.UntimedTodos.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadTodosForDate_EveryTaskAppearsExactlyOnce()
    {
        var viewModel = NewViewModel(Task("A", 8), Task("B"), Task("C", 23), Task("D", 2));

        await viewModel.LoadTodosForDateAsync(Day);

        var placed = viewModel.HourSlots.SelectMany(s => s.Items).Concat(viewModel.UntimedTodos).ToList();
        placed.Should().HaveCount(4);
        placed.Select(t => t.TaskDescription).Should().BeEquivalentTo(new[] { "A", "B", "C", "D" });
    }

    [Fact]
    public async Task LoadTodosForDate_ReloadingDoesNotDuplicateRows()
    {
        var viewModel = NewViewModel(Task("Gym", 8));

        await viewModel.LoadTodosForDateAsync(Day);
        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.HourSlots.Should().HaveCount(19, "the grid is rebuilt, not appended to");
        viewModel.HourSlots.Single(s => s.Hour == 8).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearTodos_EmptiesTheGridAndTheTray()
    {
        var viewModel = NewViewModel(Task("Gym", 8), Task("Milk"));
        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.ClearTodos();

        viewModel.HourSlots.Should().OnlyContain(s => s.IsEmpty);
        viewModel.UntimedTodos.Should().BeEmpty();
        viewModel.HasUntimedTodos.Should().BeFalse();
    }

    [Fact]
    public async Task AddAtHour_OpensTheFormCarryingThatHour()
    {
        var navigation = new Mock<INavigationService>();
        var service = new Mock<ITodoService>();
        service.Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
               .ReturnsAsync(new List<TodoItem> { Task("Gym", 8) });
        service.Setup(s => s.GetRecurringTasksAsync()).ReturnsAsync(new List<RecurringTask>());
        var viewModel = new PlannerSectionViewModel(
            service.Object, navigation.Object, new Mock<IDialogService>().Object);
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.AddAtHourCommand.ExecuteAsync(viewModel.HourSlots.Single(s => s.Hour == 14));

        navigation.Verify(n => n.NavigateToAsync(It.Is<string>(r => r.Contains("Hour=14"))), Times.Once);
    }
}
