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
        viewModel.CalendarDays.Count.Should().BeOneOf(35, 42);
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

    [Fact]
    public void Constructor_ShouldInitializeDefaultQuickMenu()
    {
        // Act
        var viewModel = new MainViewModel(_diaryServiceMock.Object);

        // Assert
        viewModel.QuickMenuItems.Should().HaveCount(6);
        viewModel.QuickMenuItems[0].Id.Should().Be("Reading");
        viewModel.QuickMenuItems[1].Id.Should().Be("Moments");
        viewModel.QuickMenuItems[2].Id.Should().Be("Deeds");
        viewModel.QuickMenuItems[3].Id.Should().Be("Habits");
        viewModel.QuickMenuItems[4].Id.Should().Be("Wishlist");
        viewModel.QuickMenuItems[5].Id.Should().Be("Finance");
    }

    [Fact]
    public async Task LoadQuickMenuAsync_WithCustomOrder_ShouldReorderMenu()
    {
        // Arrange
        var customOrder = new List<string> { "Finance", "Wishlist", "Reading", "Moments", "Deeds", "Habits" };
        var profile = new UserProfile { QuickMenuOrder = customOrder };
        
        _diaryServiceMock
            .Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(profile);

        var viewModel = new MainViewModel(_diaryServiceMock.Object);

        // Act
        // LoadQuickMenuAsync is called asynchronously in constructor, we just need to give it a moment to run
        await Task.Delay(100);

        // Assert
        viewModel.QuickMenuItems.Should().HaveCount(6);
        viewModel.QuickMenuItems[0].Id.Should().Be("Finance");
        viewModel.QuickMenuItems[1].Id.Should().Be("Wishlist");
        viewModel.QuickMenuItems[2].Id.Should().Be("Reading");
    }

    [Fact]
    public async Task ReorderMenuAsync_ShouldSwapItemsAndSaveToProfile()
    {
        // Arrange
        var profile = new UserProfile();
        _diaryServiceMock
            .Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(profile);

        var viewModel = new MainViewModel(_diaryServiceMock.Object);
        await Task.Delay(50); // let init finish

        var itemToDrag = viewModel.QuickMenuItems[0]; // Reading
        var targetItem = viewModel.QuickMenuItems[2]; // Deeds

        viewModel.DragMenuStartingCommand.Execute(itemToDrag);

        // Act
        await viewModel.ReorderMenuCommand.ExecuteAsync(targetItem);

        // Assert
        viewModel.QuickMenuItems[2].Id.Should().Be("Reading");
        
        _diaryServiceMock.Verify(s => s.SaveUserProfileAsync(It.Is<UserProfile>(p => 
            p.QuickMenuOrder != null && 
            p.QuickMenuOrder.Count == 6 &&
            p.QuickMenuOrder[2] == "Reading")), Times.Once);
    }
}
