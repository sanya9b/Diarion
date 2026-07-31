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

    private static PlannerSectionViewModel NewViewModel(params TodoItem[] todos)
    {
        var service = new Mock<ITodoService>();
        service.Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>())).ReturnsAsync(todos.ToList());
        return new PlannerSectionViewModel(service.Object, new Mock<INavigationService>().Object);
    }

    [Fact]
    public async Task LoadTodosForDate_BuildsOneRowPerHourFromSevenToTwentyThree()
    {
        var viewModel = NewViewModel();

        await viewModel.LoadTodosForDateAsync(Day);

        viewModel.HourSlots.Should().HaveCount(17);
        viewModel.HourSlots.First().Hour.Should().Be(7);
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
    [InlineData(0, 7)]    // small hours clamp up to the first row
    [InlineData(6, 7)]
    [InlineData(7, 7)]
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

        viewModel.HourSlots.Should().HaveCount(17, "the grid is rebuilt, not appended to");
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
        var viewModel = new PlannerSectionViewModel(service.Object, navigation.Object);
        await viewModel.LoadTodosForDateAsync(Day);

        await viewModel.AddAtHourCommand.ExecuteAsync(viewModel.HourSlots.Single(s => s.Hour == 14));

        navigation.Verify(n => n.NavigateToAsync(It.Is<string>(r => r.Contains("Hour=14"))), Times.Once);
    }
}
