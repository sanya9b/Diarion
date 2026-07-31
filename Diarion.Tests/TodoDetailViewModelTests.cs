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

public class TodoDetailViewModelTests
{
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly TodoDetailViewModel _viewModel;

    public TodoDetailViewModelTests()
    {
        _todoServiceMock = new Mock<ITodoService>();
        _viewModel = new TodoDetailViewModel(_todoServiceMock.Object, new Mock<INavigationService>().Object, new Mock<IDialogService>().Object);
    }

    [Fact]
    public async Task SaveAsync_WhenDescriptionIsEmpty_DoesNotSave()
    {
        // Arrange
        _viewModel.TaskDescription = "  ";

        // Act
        await _viewModel.SaveAsync();

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_WhenValid_SavesTodo()
    {
        // Arrange
        _viewModel.TaskDescription = "My Task";
        _viewModel.SelectedPriority = TodoPriority.Medium;

        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());

        // Exception will be thrown inside SaveAsync at Shell.Current.GoToAsync("..") 
        // since Shell.Current is null in test environment. 
        // We can catch it to verify the logic before it.
        try
        {
            // Act
            await _viewModel.SaveAsync();
        }
        catch (NullReferenceException)
        {
            // Expected because Shell.Current is null
        }

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "My Task")), Times.Once);
    }

    /// <summary>Saves and swallows the navigation that follows: Shell.Current is null under test.</summary>
    private async Task SaveIgnoringNavigationAsync()
    {
        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());
        try { await _viewModel.SaveAsync(); }
        catch (NullReferenceException) { }
    }

    [Fact]
    public async Task SaveAsync_WithAWeeklyRepeat_SendsThatRuleToTheService()
    {
        _viewModel.TaskDescription = "Стретчинг";
        _viewModel.IsRecurring = true;
        _viewModel.SetRecurrenceKindCommand.Execute(nameof(RecurrenceKind.Weekly));
        _viewModel.Weekdays.Single(d => d.DayOfWeek == (int)DayOfWeek.Tuesday).IsSelected = true;

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SetRecurrenceAsync(
            It.IsAny<Guid>(),
            It.Is<RecurrenceRule>(r => r.Kind == RecurrenceKind.Weekly && r.DaysOfWeek.Contains((int)DayOfWeek.Tuesday))),
            Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WithRepeatUnchecked_EndsTheSeries()
    {
        _viewModel.TaskDescription = "Стретчинг";
        _viewModel.IsRecurring = false;

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SetRecurrenceAsync(It.IsAny<Guid>(), null), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WeeklyWithNoDayChosen_RefusesAndSaysWhy()
    {
        // Such a rule never fires, so saving it would read as success and then do nothing at all.
        _viewModel.TaskDescription = "Стретчинг";
        _viewModel.IsRecurring = true;
        _viewModel.SetRecurrenceKindCommand.Execute(nameof(RecurrenceKind.Weekly));

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
        _viewModel.RecurrenceError.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SaveAsync_WithAnEndDate_CarriesItOntoTheRule()
    {
        _viewModel.TaskDescription = "Стретчинг";
        _viewModel.IsRecurring = true;
        _viewModel.HasRecurrenceEnd = true;
        _viewModel.RecurrenceEndDate = new DateTime(2026, 12, 31);

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SetRecurrenceAsync(
            It.IsAny<Guid>(),
            It.Is<RecurrenceRule>(r => r.EndDate == new DateTime(2026, 12, 31))),
            Times.Once);
    }

    [Fact]
    public async Task LoadingARecurringTask_FillsThePickerFromItsRule()
    {
        var ruleId = Guid.NewGuid();
        var todo = new TodoItem { Id = Guid.NewGuid(), TaskDescription = "Стретчинг", RecurringTaskId = ruleId };
        _todoServiceMock.Setup(s => s.GetTodoByIdAsync(todo.Id)).ReturnsAsync(todo);
        _todoServiceMock.Setup(s => s.GetRecurringTaskAsync(ruleId)).ReturnsAsync(new RecurringTask
        {
            Id = ruleId,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.IntervalDays, EveryN = 3 }
        });

        _viewModel.TodoId = todo.Id.ToString();
        await Task.Delay(50);   // OnTodoIdChanged is async void, as the toolkit generates it

        _viewModel.IsRecurring.Should().BeTrue();
        _viewModel.RecurrenceKind.Should().Be(RecurrenceKind.IntervalDays);
        _viewModel.EveryNDays.Should().Be(3);
    }

    [Fact]
    public void ChoosingAWeekdayImpliesAWeeklyRule()
    {
        _viewModel.ToggleWeekdayCommand.Execute(_viewModel.Weekdays[0]);

        _viewModel.RecurrenceKind.Should().Be(RecurrenceKind.Weekly);
    }

    [Fact]
    public void SelectPriority_UpdatesSelectedPriorityAndItems()
    {
        // Arrange
        var lowPriorityItem = _viewModel.PrioritiesList[0]; // Low
        var highPriorityItem = _viewModel.PrioritiesList[2]; // High

        // Act
        _viewModel.SelectPriorityCommand.Execute(highPriorityItem);

        // Assert
        _viewModel.SelectedPriority.Should().Be(TodoPriority.High);
        highPriorityItem.IsSelected.Should().BeTrue();
        lowPriorityItem.IsSelected.Should().BeFalse();
    }
}