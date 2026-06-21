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

        _todoServiceMock
            .Setup(s => s.GetTodosForDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());
            
        _diaryServiceMock
            .Setup(s => s.GetEntryForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DiaryEntry());
    }

    [Fact]
    public void SwitchModes_ChangesPropertiesCorrectly()
    {
        // Arrange
        var menstrualCycleServiceMock = new Mock<IMenstrualCycleService>();
        menstrualCycleServiceMock
            .Setup(s => s.GetCycleInfoForDate(It.IsAny<DateTime>(), It.IsAny<UserProfile>()))
            .Returns(new CycleDayInfo());
        var calendarServiceMock = new Mock<ICalendarService>();
        calendarServiceMock.Setup(s => s.GenerateCalendarDays(It.IsAny<DateTime>())).Returns(new List<CalendarDay>());
        var menuConfigServiceMock = new Mock<IMenuConfigurationService>();
        menuConfigServiceMock.Setup(s => s.GetDefaultMenuItems()).Returns(new List<QuickMenuItem>());
        var viewModel = new MainViewModel(_diaryServiceMock.Object, _profileServiceMock.Object, _todoServiceMock.Object, _habitServiceMock.Object, menstrualCycleServiceMock.Object, calendarServiceMock.Object, menuConfigServiceMock.Object);

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
        var menstrualCycleServiceMock = new Mock<IMenstrualCycleService>();
        menstrualCycleServiceMock
            .Setup(s => s.GetCycleInfoForDate(It.IsAny<DateTime>(), It.IsAny<UserProfile>()))
            .Returns(new CycleDayInfo());
        
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var todayDay = new CalendarDay { Date = today, IsSelected = true };
        var tomorrowDay = new CalendarDay { Date = tomorrow, IsSelected = false };
        var days = new List<CalendarDay> { todayDay, tomorrowDay };
        
        var calendarServiceMock = new Mock<ICalendarService>();
        calendarServiceMock.Setup(s => s.GenerateCalendarDays(It.IsAny<DateTime>())).Returns(days);
        
        var menuConfigServiceMock = new Mock<IMenuConfigurationService>();
        menuConfigServiceMock.Setup(s => s.GetDefaultMenuItems()).Returns(new List<QuickMenuItem>());

        var viewModel = new MainViewModel(_diaryServiceMock.Object, _profileServiceMock.Object, _todoServiceMock.Object, _habitServiceMock.Object, menstrualCycleServiceMock.Object, calendarServiceMock.Object, menuConfigServiceMock.Object);

        // Act
        await viewModel.SelectDateCommand.ExecuteAsync(tomorrowDay);

        // Assert
        tomorrowDay.IsSelected.Should().BeTrue();
        todayDay.IsSelected.Should().BeFalse();
    }
}