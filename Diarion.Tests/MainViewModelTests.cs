using System;
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

    public MainViewModelTests()
    {
        _diaryServiceMock = new Mock<IDiaryService>();
        _diaryServiceMock
            .Setup(s => s.GetTodosForMonthAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<TodoItem>());
        _diaryServiceMock
            .Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile());
        _diaryServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());
        _diaryServiceMock
            .Setup(s => s.GetEntryForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DiaryEntry());
    }

    [Fact]
    public void Constructor_ShouldGenerateCalendarAndSetDefaultMode()
    {
        // Act
        var viewModel = new MainViewModel(_diaryServiceMock.Object);

        // Assert
        viewModel.CalendarDays.Should().HaveCount(42);
        viewModel.IsDiaryMode.Should().BeTrue();
        viewModel.IsPlannerMode.Should().BeFalse();
        viewModel.CurrentEntry.Should().BeNull();
    }

    [Fact]
    public void GenerateCalendar_ShouldMarkCurrentDateAsSelected()
    {
        // Arrange
        var viewModel = new MainViewModel(_diaryServiceMock.Object);
        var today = DateTime.Today;

        // Assert
        var selectedDay = viewModel.CalendarDays.SingleOrDefault(d => d.IsSelected);
        selectedDay.Should().NotBeNull();
        selectedDay!.Date.Date.Should().Be(today);
    }

    [Fact]
    public async Task SwitchToPlannerMode_ShouldLoadTodos()
    {
        // Arrange
        _diaryServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem> { new TodoItem { TaskDescription = "Test Todo" } });

        var viewModel = new MainViewModel(_diaryServiceMock.Object);

        // Act
        await viewModel.SwitchToPlannerModeCommand.ExecuteAsync(null);

        // Assert
        viewModel.IsPlannerMode.Should().BeTrue();
        viewModel.IsDiaryMode.Should().BeFalse();
        viewModel.Todos.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadEntriesAsync_ShouldLoadEntryForSelectedDate()
    {
        // Arrange
        _diaryServiceMock
            .Setup(s => s.GetEntryForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DiaryEntry { Title = "Test Entry" });

        var viewModel = new MainViewModel(_diaryServiceMock.Object);

        // Act
        await viewModel.LoadEntriesAsync();

        // Assert
        viewModel.CurrentEntry.Should().NotBeNull();
        viewModel.CurrentEntry!.Title.Should().Be("Test Entry");
        viewModel.IsBusy.Should().BeFalse();
    }
}
