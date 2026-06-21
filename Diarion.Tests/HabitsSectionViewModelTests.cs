using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class HabitsSectionViewModelTests
{
    private readonly Mock<IHabitService> _habitServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<ICalendarService> _calendarServiceMock;
    private readonly Mock<IMenstrualCycleService> _menstrualCycleServiceMock;
    private readonly Mock<IProfileService> _profileServiceMock;
    private readonly Mock<ITodoService> _todoServiceMock;
    
    private readonly CalendarSectionViewModel _calendarSection;
    private readonly HabitsSectionViewModel _viewModel;

    public HabitsSectionViewModelTests()
    {
        _habitServiceMock = new Mock<IHabitService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _calendarServiceMock = new Mock<ICalendarService>();
        _menstrualCycleServiceMock = new Mock<IMenstrualCycleService>();
        _profileServiceMock = new Mock<IProfileService>();
        _todoServiceMock = new Mock<ITodoService>();

        _calendarSection = new CalendarSectionViewModel(
            _calendarServiceMock.Object,
            _menstrualCycleServiceMock.Object,
            _profileServiceMock.Object,
            _todoServiceMock.Object);

        _viewModel = new HabitsSectionViewModel(
            _habitServiceMock.Object,
            _dialogServiceMock.Object,
            _calendarSection);
    }

    [Fact]
    public void ToggleEditHabitsMode_TogglesValue()
    {
        // Act
        _viewModel.ToggleEditHabitsModeCommand.Execute(null);

        // Assert
        _viewModel.IsEditHabitsMode.Should().BeTrue();

        // Act
        _viewModel.ToggleEditHabitsModeCommand.Execute(null);

        // Assert
        _viewModel.IsEditHabitsMode.Should().BeFalse();
    }

    [Fact]
    public async Task AddHabitAsync_WhenDialogReturnsValue_AddsHabit()
    {
        // Arrange
        _dialogServiceMock
            .Setup(s => s.ShowPromptAsync(It.IsAny<string>(), It.IsAny<string>(), "OK", "Cancel"))
            .ReturnsAsync("Read a book");

        _viewModel.Entry = new DiaryEntryViewModel(new DiaryEntry());

        // Act
        await _viewModel.AddHabitCommand.ExecuteAsync(null);

        // Assert
        _habitServiceMock.Verify(s => s.AddHabitDefinitionAsync(It.Is<HabitDefinition>(h => h.Name == "Read a book")), Times.Once);
        _viewModel.Entry.Habits.Should().HaveCount(1);
        _viewModel.Entry.Habits[0].Name.Should().Be("Read a book");
    }

    [Fact]
    public async Task AddHabitAsync_WhenDialogReturnsEmpty_DoesNotAddHabit()
    {
        // Arrange
        _dialogServiceMock
            .Setup(s => s.ShowPromptAsync(It.IsAny<string>(), It.IsAny<string>(), "OK", "Cancel"))
            .ReturnsAsync("   ");

        _viewModel.Entry = new DiaryEntryViewModel(new DiaryEntry());

        // Act
        await _viewModel.AddHabitCommand.ExecuteAsync(null);

        // Assert
        _habitServiceMock.Verify(s => s.AddHabitDefinitionAsync(It.IsAny<HabitDefinition>()), Times.Never);
        _viewModel.Entry.Habits.Should().BeEmpty();
    }
}
