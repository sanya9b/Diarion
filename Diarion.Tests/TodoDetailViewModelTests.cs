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
    public async Task TypingASchedulePutsItIntoTheFormAndCutsItOutOfTheTitle()
    {
        _viewModel.TaskDescription = "щовівторка о 18:00 теніс";

        _viewModel.IsRecurring.Should().BeTrue();
        _viewModel.RecurrenceKind.Should().Be(RecurrenceKind.Weekly);
        _viewModel.Weekdays.Single(d => d.DayOfWeek == (int)DayOfWeek.Tuesday).IsSelected.Should().BeTrue();
        _viewModel.HasTime.Should().BeTrue();
        _viewModel.TargetTime.Should().Be(new TimeSpan(18, 0, 0));
        _viewModel.HasParseHint.Should().BeTrue();

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "теніс")), Times.Once);
    }

    // --- a task that holds a stretch of the day ---

    [Fact]
    public async Task SaveAsync_WithARange_StoresBothEnds()
    {
        _viewModel.TaskDescription = "Зустріч";
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(13, 0, 0);
        _viewModel.HasEndTime = true;
        _viewModel.EndTime = new TimeSpan(16, 0, 0);

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(
            It.Is<TodoItem>(t => t.TargetTime == new TimeSpan(13, 0, 0)
                              && t.EndTime == new TimeSpan(16, 0, 0))), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WithoutARange_StoresNoEnd()
    {
        // Null rather than "equal to the start": null is what every reader treats as a point task, and it
        // is also what a row written before ranges existed deserializes to.
        _viewModel.TaskDescription = "Дзвінок";
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(15, 0, 0);

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.EndTime == null)), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ARangeEndingBeforeItStarts_IsRefusedWithAReason()
    {
        _viewModel.TaskDescription = "Зустріч";
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(13, 0, 0);
        _viewModel.HasEndTime = true;
        // Straight onto the backing property: setting the start would otherwise nudge this forward.
        _viewModel.EndTime = new TimeSpan(11, 0, 0);

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
        _viewModel.HasTimeRangeError.Should().BeTrue();
    }

    [Fact]
    public void SwitchingTheRangeOnDefaultsToAnHour()
    {
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(13, 0, 0);

        _viewModel.HasEndTime = true;

        _viewModel.EndTime.Should().Be(new TimeSpan(14, 0, 0));
    }

    [Fact]
    public void MovingTheStartPastTheEndCarriesTheEndAlong()
    {
        // Dragging the start of a block is a request to move the block, not a mistake to be argued with.
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(13, 0, 0);
        _viewModel.HasEndTime = true;

        _viewModel.TargetTime = new TimeSpan(18, 0, 0);

        _viewModel.EndTime.Should().Be(new TimeSpan(19, 0, 0));
    }

    [Fact]
    public void DroppingTheTimeDropsTheRangeWithIt()
    {
        _viewModel.HasTime = true;
        _viewModel.HasEndTime = true;

        _viewModel.HasTime = false;

        _viewModel.HasEndTime.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_AfterTheRangeIsSwitchedOff_StoresNoEnd()
    {
        _viewModel.TaskDescription = "Зустріч";
        _viewModel.HasTime = true;
        _viewModel.TargetTime = new TimeSpan(13, 0, 0);
        _viewModel.HasEndTime = true;
        _viewModel.HasEndTime = false;

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.EndTime == null)), Times.Once);
    }

    [Fact]
    public async Task TypingARangePutsBothEndsIntoTheForm()
    {
        _viewModel.TaskDescription = "з 13:00 до 16:00 зустріч";

        _viewModel.HasTime.Should().BeTrue();
        _viewModel.TargetTime.Should().Be(new TimeSpan(13, 0, 0));
        _viewModel.HasEndTime.Should().BeTrue();
        _viewModel.EndTime.Should().Be(new TimeSpan(16, 0, 0));

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "зустріч")), Times.Once);
    }

    [Fact]
    public void UndoingAReadRangeLeavesTheFormAsItWas()
    {
        _viewModel.TaskDescription = "з 13:00 до 16:00 зустріч";
        _viewModel.DismissParseCommand.Execute(null);

        _viewModel.HasTime.Should().BeFalse();
        _viewModel.HasEndTime.Should().BeFalse();
    }

    [Fact]
    public void ANamedHourTurnsTheReminderOn()
    {
        // Saying an hour out loud is asking to be told about it; typing one into the picker is not.
        _viewModel.TaskDescription = "щодня о 9:00 таблетки";

        _viewModel.HasReminder.Should().BeTrue();
    }

    [Fact]
    public void PlainTitleLeavesTheFormAlone()
    {
        _viewModel.TaskDescription = "подзвонити стоматологу";

        _viewModel.IsRecurring.Should().BeFalse();
        _viewModel.HasParseHint.Should().BeFalse();
        _viewModel.HasReminder.Should().BeFalse();
    }

    [Fact]
    public async Task UndoPutsTheScheduleBackAndKeepsTheTitleWhole()
    {
        _viewModel.TaskDescription = "щовівторка о 18:00 теніс";
        _viewModel.DismissParseCommand.Execute(null);

        _viewModel.IsRecurring.Should().BeFalse();
        _viewModel.HasTime.Should().BeFalse();
        _viewModel.HasReminder.Should().BeFalse();
        _viewModel.HasParseHint.Should().BeFalse();

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(
            s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "щовівторка о 18:00 теніс")), Times.Once);
    }

    [Fact]
    public void OnceUndoneTheTextIsNotReadAgain()
    {
        _viewModel.TaskDescription = "щовівторка теніс";
        _viewModel.DismissParseCommand.Execute(null);

        _viewModel.TaskDescription = "щочетверга теніс";

        _viewModel.IsRecurring.Should().BeFalse();
        _viewModel.HasParseHint.Should().BeFalse();
    }

    [Fact]
    public void AChipTappedByHandIsNotOverwrittenByTheParser()
    {
        _viewModel.SetRecurrenceKindCommand.Execute(nameof(RecurrenceKind.MonthlyByDay));

        _viewModel.TaskDescription = "щовівторка теніс";

        _viewModel.RecurrenceKind.Should().Be(RecurrenceKind.MonthlyByDay);
    }

    [Fact]
    public async Task ATitleThatIsNothingButASchedulKeepsItsWords()
    {
        // Cutting "щовівторка" out of "щовівторка" leaves a task with no name at all, which is worse
        // than a badly named one.
        _viewModel.TaskDescription = "щовівторка";

        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "щовівторка")), Times.Once);
    }

    [Fact]
    public async Task UncheckingRepeatOnAnExistingSeriesEndsIt()
    {
        // The path the user actually walks: open an occurrence that already belongs to a series, untick
        // the box, save. Distinct from unticking on a brand-new task, which is what the other test covers.
        var ruleId = Guid.NewGuid();
        var todo = new TodoItem
        {
            Id = Guid.NewGuid(),
            TaskDescription = "Стретчинг",
            TargetDate = DateTime.Today,
            RecurringTaskId = ruleId
        };
        _todoServiceMock.Setup(s => s.GetTodoByIdAsync(todo.Id)).ReturnsAsync(todo);
        _todoServiceMock.Setup(s => s.GetRecurringTaskAsync(ruleId)).ReturnsAsync(new RecurringTask
        {
            Id = ruleId,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = DateTime.Today.AddDays(-5) }
        });

        _viewModel.TodoId = todo.Id.ToString();
        await Task.Delay(50);   // OnTodoIdChanged is async void, as the toolkit generates it
        _viewModel.IsRecurring.Should().BeTrue("the form should open showing the task as repeating");

        _viewModel.IsRecurring = false;
        await SaveIgnoringNavigationAsync();

        _todoServiceMock.Verify(s => s.SetRecurrenceAsync(todo.Id, null), Times.Once);
    }

    [Fact]
    public async Task ReopeningAnOccurrenceOfAnEndedSeriesShowsTheRepeatAsOff()
    {
        // The bug the user reported: the box came back ticked every time. A row keeps its rule id after
        // the series ends, and the form read the box off that id alone.
        var ruleId = Guid.NewGuid();
        var todo = new TodoItem
        {
            Id = Guid.NewGuid(),
            TaskDescription = "Стретчинг",
            TargetDate = DateTime.Today,
            RecurringTaskId = ruleId
        };
        _todoServiceMock.Setup(s => s.GetTodoByIdAsync(todo.Id)).ReturnsAsync(todo);
        _todoServiceMock.Setup(s => s.GetRecurringTaskAsync(ruleId)).ReturnsAsync(new RecurringTask
        {
            Id = ruleId,
            Recurrence = new RecurrenceRule
            {
                Kind = RecurrenceKind.Daily,
                Anchor = DateTime.Today.AddDays(-5),
                EndDate = DateTime.Today.AddDays(-1)
            }
        });

        _viewModel.TodoId = todo.Id.ToString();
        await Task.Delay(50);   // OnTodoIdChanged is async void, as the toolkit generates it

        _viewModel.IsRecurring.Should().BeFalse();
        // And the ended rule's "stops yesterday" must not reach the form, or the next save writes it
        // straight back and the switch-off looks like it did nothing again.
        _viewModel.HasRecurrenceEnd.Should().BeFalse();
    }

    [Fact]
    public async Task AnOccurrenceFromBeforeTheSeriesEndedStillShowsAsRepeating()
    {
        // Unticking on Wednesday ends the series on Tuesday. Monday's occurrence belongs to the days it
        // really did run, so it keeps the box — matching the ↻ the list draws for that same day.
        var ruleId = Guid.NewGuid();
        var todo = new TodoItem
        {
            Id = Guid.NewGuid(),
            TaskDescription = "Стретчинг",
            TargetDate = DateTime.Today.AddDays(-3),
            RecurringTaskId = ruleId
        };
        _todoServiceMock.Setup(s => s.GetTodoByIdAsync(todo.Id)).ReturnsAsync(todo);
        _todoServiceMock.Setup(s => s.GetRecurringTaskAsync(ruleId)).ReturnsAsync(new RecurringTask
        {
            Id = ruleId,
            Recurrence = new RecurrenceRule
            {
                Kind = RecurrenceKind.Daily,
                Anchor = DateTime.Today.AddDays(-5),
                EndDate = DateTime.Today.AddDays(-1)
            }
        });

        _viewModel.TodoId = todo.Id.ToString();
        await Task.Delay(50);   // OnTodoIdChanged is async void, as the toolkit generates it

        _viewModel.IsRecurring.Should().BeTrue();
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