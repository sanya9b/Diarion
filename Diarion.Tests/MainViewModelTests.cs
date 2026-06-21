using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class MainViewModelTests
{
    private readonly Mock<IDiaryService> _diaryServiceMock;
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly Mock<IHabitService> _habitServiceMock;
    private readonly Mock<IProfileService> _profileServiceMock;
    
    public MainViewModelTests()
    {
        _diaryServiceMock = new Mock<IDiaryService>();
        _todoServiceMock = new Mock<ITodoService>();
        _habitServiceMock = new Mock<IHabitService>();
        _profileServiceMock = new Mock<IProfileService>();

        _todoServiceMock
            .Setup(s => s.GetTodosForMonthAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<TodoItem>());

        _profileServiceMock
            .Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile());

        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());
            
        _diaryServiceMock
            .Setup(s => s.GetEntryForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DiaryEntry());
    }

    [Fact]
    public void SwitchModes_ChangesPropertiesCorrectly()
    {
        // Arrange
        var viewModel = new MainViewModel(_diaryServiceMock.Object, _profileServiceMock.Object, _todoServiceMock.Object, _habitServiceMock.Object);

        // Act - Switch to Planner
        viewModel.SwitchToPlannerModeCommand.Execute(null);

        // Assert
        viewModel.IsPlannerMode.Should().BeTrue();
        viewModel.IsDiaryMode.Should().BeFalse();

        // Act - Switch back to Diary
        viewModel.SwitchToDiaryModeCommand.Execute(null);

        // Assert
        viewModel.IsPlannerMode.Should().BeFalse();
        viewModel.IsDiaryMode.Should().BeTrue();
    }

    [Fact]
    public async Task SelectDate_UpdatesSelection()
    {
        // Arrange
        var viewModel = new MainViewModel(_diaryServiceMock.Object, _profileServiceMock.Object, _todoServiceMock.Object, _habitServiceMock.Object);
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        
        // We need the calendar generated first (happens in ctor)
        var todayDay = viewModel.CalendarDays.First(d => d.Date.Date == today);
        var tomorrowDay = viewModel.CalendarDays.First(d => d.Date.Date == tomorrow);

        // Act
        await viewModel.SelectDateCommand.ExecuteAsync(tomorrowDay);

        // Assert
        tomorrowDay.IsSelected.Should().BeTrue();
        todayDay.IsSelected.Should().BeFalse();
    }
}