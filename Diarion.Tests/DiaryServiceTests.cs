using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class DiaryServiceTests : IDisposable
{
    private readonly DiaryService _diaryService;

    public DiaryServiceTests()
    {
        _diaryService = new DiaryService(useInMemory: true);
    }

    private async Task ClearDatabaseAsync()
    {
        var todos = await _diaryService.GetAllTodosAsync();
        foreach (var t in todos)
        {
            await _diaryService.DeleteTodoAsync(t.Id);
        }
        var entries = await _diaryService.GetAllEntriesAsync();
        foreach (var e in entries)
        {
            await _diaryService.DeleteEntryAsync(e.Id);
        }
    }

    [Fact]
    public async Task SaveEntryAsync_ShouldSaveNewEntry()
    {
        // Arrange
        var entry = new DiaryEntry
        {
            Title = "Test Entry",
            Content = "Test Content",
            Emotion = Emotion.Happy
        };

        // Act
        await _diaryService.SaveEntryAsync(entry);
        var fetchedEntry = await _diaryService.GetEntryByIdAsync(entry.Id);

        // Assert
        fetchedEntry.Should().NotBeNull();
        fetchedEntry.Title.Should().Be("Test Entry");
        fetchedEntry.Emotion.Should().Be(Emotion.Happy);

        // Cleanup (optional since it's in-memory, but good practice)
        await _diaryService.DeleteEntryAsync(entry.Id);
    }

    [Fact]
    public async Task SaveTodoAsync_ShouldSaveAndRetrieveTodo()
    {
        // Arrange
        var targetDate = new DateTime(2025, 1, 1);
        var todo = new TodoItem
        {
            TaskDescription = "Write tests",
            TargetDate = targetDate,
            Priority = TodoPriority.High
        };

        // Act
        await _diaryService.SaveTodoAsync(todo);
        var todos = await _diaryService.GetTodosForDateAsync(targetDate);

        // Assert
        todos.Should().NotBeEmpty();
        todos.Should().ContainSingle(t => t.TaskDescription == "Write tests");
        todos[0].Priority.Should().Be(TodoPriority.High);
    }

    [Fact]
    public async Task SaveUserProfileAsync_WithZeroCycleValues_NormalizesDefaults()
    {
        // Arrange
        var profile = new UserProfile
        {
            IsMenstrualTrackingEnabled = true,
            CycleLength = 0,
            PeriodLength = 0,
            LastPeriodStartDate = new DateTime(2025, 1, 1)
        };

        // Act
        await _diaryService.SaveUserProfileAsync(profile);
        var savedProfile = await _diaryService.GetUserProfileAsync();

        // Assert
        savedProfile.CycleLength.Should().Be(UserProfile.DefaultCycleLength);
        savedProfile.PeriodLength.Should().Be(UserProfile.DefaultPeriodLength);
    }

    [Fact]
    public async Task SaveUserProfileAsync_WithPeriodLongerThanCycle_ClampsPeriodLength()
    {
        // Arrange
        var profile = new UserProfile
        {
            CycleLength = 21,
            PeriodLength = 30
        };

        // Act
        await _diaryService.SaveUserProfileAsync(profile);
        var savedProfile = await _diaryService.GetUserProfileAsync();

        // Assert
        savedProfile.CycleLength.Should().Be(21);
        savedProfile.PeriodLength.Should().Be(21);
    }

    [Fact]
    public async Task GetTodosForDateAsync_ShouldAutoMigrateUncompletedTasksToToday()
    {
        await ClearDatabaseAsync();

        // Arrange
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var todo = new TodoItem
        {
            TaskDescription = "Yesterday Task",
            TargetDate = yesterday,
            IsCompleted = false
        };
        await _diaryService.SaveTodoAsync(todo);

        // Ensure setting is true (it is by default, but let's be explicit)
        var profile = await _diaryService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _diaryService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _diaryService.GetTodosForDateAsync(today);

        // Assert
        todos.Should().ContainSingle(t => t.TaskDescription == "Yesterday Task");
        todos[0].TargetDate.Should().Be(today);
    }

    [Fact]
    public async Task GetTodosForDateAsync_ShouldNotAutoMigrateIfSettingIsFalse()
    {
        await ClearDatabaseAsync();

        // Arrange
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var todo = new TodoItem
        {
            TaskDescription = "Yesterday Task",
            TargetDate = yesterday,
            IsCompleted = false
        };
        await _diaryService.SaveTodoAsync(todo);

        var profile = await _diaryService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = false;
        await _diaryService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _diaryService.GetTodosForDateAsync(today);

        // Assert
        todos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTodosForDateAsync_ShouldDowngradeMigratedHighPriorityTaskToMedium_WhenLimitReached()
    {
        await ClearDatabaseAsync();

        // Arrange
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        // Create 3 High-priority tasks for Today
        for (int i = 0; i < 3; i++)
        {
            await _diaryService.SaveTodoAsync(new TodoItem
            {
                TaskDescription = $"Today Task {i}",
                TargetDate = today,
                Priority = TodoPriority.High
            });
        }

        // Create 1 High-priority task for Yesterday (uncompleted)
        var pastTodo = new TodoItem
        {
            TaskDescription = "Yesterday High Priority Task",
            TargetDate = yesterday,
            IsCompleted = false,
            Priority = TodoPriority.High
        };
        await _diaryService.SaveTodoAsync(pastTodo);

        var profile = await _diaryService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _diaryService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _diaryService.GetTodosForDateAsync(today);

        // Assert
        todos.Should().HaveCount(4);
        var migratedTask = todos.Find(t => t.TaskDescription == "Yesterday High Priority Task");
        migratedTask.Should().NotBeNull();
        migratedTask!.Priority.Should().Be(TodoPriority.Medium);
    }

    [Fact]
    public async Task GetTodosForDateAsync_ShouldNotDowngradeMigratedHighPriorityTask_WhenLimitNotReached()
    {
        await ClearDatabaseAsync();

        // Arrange
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        // Create 2 High-priority tasks for Today
        for (int i = 0; i < 2; i++)
        {
            await _diaryService.SaveTodoAsync(new TodoItem
            {
                TaskDescription = $"Today Task {i}",
                TargetDate = today,
                Priority = TodoPriority.High
            });
        }

        // Create 1 High-priority task for Yesterday
        var pastTodo = new TodoItem
        {
            TaskDescription = "Yesterday High Priority Task",
            TargetDate = yesterday,
            IsCompleted = false,
            Priority = TodoPriority.High
        };
        await _diaryService.SaveTodoAsync(pastTodo);

        var profile = await _diaryService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _diaryService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _diaryService.GetTodosForDateAsync(today);

        // Assert
        todos.Should().HaveCount(3);
        var migratedTask = todos.Find(t => t.TaskDescription == "Yesterday High Priority Task");
        migratedTask.Should().NotBeNull();
        migratedTask!.Priority.Should().Be(TodoPriority.High); // Limit not exceeded, so it remains High
    }

    [Fact]
    public async Task GetTodosForDateAsync_ShouldDowngradeRepeatingHighPriorityTaskToMedium_WhenLimitReached()
    {
        await ClearDatabaseAsync();

        // Arrange
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        // Create 3 High-priority tasks for Today
        for (int i = 0; i < 3; i++)
        {
            await _diaryService.SaveTodoAsync(new TodoItem
            {
                TaskDescription = $"Today Task {i}",
                TargetDate = today,
                Priority = TodoPriority.High
            });
        }

        // Create 1 High-priority daily repeating task from Yesterday
        var repeatingTodo = new TodoItem
        {
            TaskDescription = "Repeating High Priority Task",
            TargetDate = yesterday,
            IsDailyRepeat = true,
            RepeatGroupId = Guid.NewGuid().ToString(),
            Priority = TodoPriority.High
        };
        await _diaryService.SaveTodoAsync(repeatingTodo);

        // Act
        var todos = await _diaryService.GetTodosForDateAsync(today);

        // Assert
        var clonedTask = todos.Find(t => t.TaskDescription == "Repeating High Priority Task" && t.TargetDate == today);
        clonedTask.Should().NotBeNull();
        clonedTask!.Priority.Should().Be(TodoPriority.Medium); // Downgraded because limit reached
    }

    public void Dispose()
    {
        _diaryService.Dispose();
    }
}
