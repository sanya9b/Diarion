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
    private readonly Mock<ICycleLogService> _cycleLogServiceMock;
    private readonly Mock<IProfileService> _profileServiceMock;
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly Mock<INavigationService> _navigationServiceMock;

    private readonly CalendarSectionViewModel _calendarSection;
    private readonly HabitsSectionViewModel _viewModel;

    public HabitsSectionViewModelTests()
    {
        _habitServiceMock = new Mock<IHabitService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _calendarServiceMock = new Mock<ICalendarService>();
        _cycleLogServiceMock = new Mock<ICycleLogService>();
        _profileServiceMock = new Mock<IProfileService>();
        _todoServiceMock = new Mock<ITodoService>();
        _navigationServiceMock = new Mock<INavigationService>();

        _calendarSection = new CalendarSectionViewModel(
            _calendarServiceMock.Object,
            _cycleLogServiceMock.Object,
            _profileServiceMock.Object,
            _todoServiceMock.Object,
            new Mock<IDispatcherService>().Object);

        _viewModel = new HabitsSectionViewModel(
            _habitServiceMock.Object,
            _dialogServiceMock.Object,
            _calendarSection,
            _navigationServiceMock.Object);
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
    public async Task AddHabitAsync_NavigatesToEditor()
    {
        await _viewModel.AddHabitCommand.ExecuteAsync(null);

        _navigationServiceMock.Verify(
            s => s.NavigateToAsync("HabitEditor", It.IsAny<System.Collections.Generic.IDictionary<string, object>>()),
            Times.Once);
    }

    [Fact]
    public async Task EditHabitAsync_NavigatesToEditorWithHabitId()
    {
        var habitId = Guid.NewGuid();
        var item = new HabitItemViewModel(new HabitItem { HabitId = habitId, Name = "Water" });

        await _viewModel.EditHabitCommand.ExecuteAsync(item);

        _navigationServiceMock.Verify(
            s => s.NavigateToAsync("HabitEditor", It.Is<System.Collections.Generic.IDictionary<string, object>>(
                d => d.ContainsKey("HabitId") && (string)d["HabitId"] == habitId.ToString())),
            Times.Once);
    }
}
