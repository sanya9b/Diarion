using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

using Diarion.Services.Database;

public class DiaryServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly DiaryService _diaryService;
    private readonly TodoService _todoService;
    private readonly ProfileService _profileService;
    private readonly HabitService _habitService;

    public DiaryServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _profileService = new ProfileService(_dbContext);
        _todoService = new TodoService(_dbContext, null);
        _habitService = new HabitService(_dbContext);
        _diaryService = new DiaryService(_dbContext, _todoService);
    }

    private async Task ClearDatabaseAsync()
    {
        var todos = await _todoService.GetAllTodosAsync();
        foreach (var t in todos)
        {
            await _todoService.DeleteTodoAsync(t.Id);
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
        await _todoService.SaveTodoAsync(todo);
        var todos = await _todoService.GetTodosForDateAsync(targetDate);

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
        await _profileService.SaveUserProfileAsync(profile);
        var savedProfile = await _profileService.GetUserProfileAsync();

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
        await _profileService.SaveUserProfileAsync(profile);
        var savedProfile = await _profileService.GetUserProfileAsync();

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
        await _todoService.SaveTodoAsync(todo);

        // Ensure setting is true (it is by default, but let's be explicit)
        var profile = await _profileService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _profileService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _todoService.GetTodosForDateAsync(today);

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
        await _todoService.SaveTodoAsync(todo);

        var profile = await _profileService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = false;
        await _profileService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _todoService.GetTodosForDateAsync(today);

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
            await _todoService.SaveTodoAsync(new TodoItem
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
        await _todoService.SaveTodoAsync(pastTodo);

        var profile = await _profileService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _profileService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _todoService.GetTodosForDateAsync(today);

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
            await _todoService.SaveTodoAsync(new TodoItem
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
        await _todoService.SaveTodoAsync(pastTodo);

        var profile = await _profileService.GetUserProfileAsync();
        profile.AutoMigrateUncompletedTasksEnabled = true;
        await _profileService.SaveUserProfileAsync(profile);

        // Act
        var todos = await _todoService.GetTodosForDateAsync(today);

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
            await _todoService.SaveTodoAsync(new TodoItem
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
        await _todoService.SaveTodoAsync(repeatingTodo);

        // Act
        var todos = await _todoService.GetTodosForDateAsync(today);

        // Assert
        var clonedTask = todos.Find(t => t.TaskDescription == "Repeating High Priority Task" && t.TargetDate == today);
        clonedTask.Should().NotBeNull();
        clonedTask!.Priority.Should().Be(TodoPriority.Medium); // Downgraded because limit reached
    }

    [Fact]
    public async Task GetCurrentStreak_WithConsecutiveDays_ReturnsCorrectStreak()
    {
        await ClearDatabaseAsync();
        var today = DateTime.Today;
        
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today });
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-1) });
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-2) });

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Should().Be(3);
    }

    [Fact]
    public async Task GetCurrentStreak_WithGap_ReturnsCorrectStreakBeforeGap()
    {
        await ClearDatabaseAsync();
        var today = DateTime.Today;
        
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today });
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-2) });

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentStreak_WhenLastEntryWasYesterday_ReturnsCorrectStreak()
    {
        await ClearDatabaseAsync();
        var today = DateTime.Today;
        
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-1) });
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-2) });

        var allEntries = await _diaryService.GetAllEntriesAsync();
        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Should().Be(2, $"Entries in DB: {allEntries.Count}. First Date: {(allEntries.Count > 0 ? allEntries[0].Date.ToString() : "none")}");
    }

    [Fact]
    public async Task GetCurrentStreak_WhenLastEntryWasOlderThanYesterday_ReturnsZero()
    {
        await ClearDatabaseAsync();
        var today = DateTime.Today;
        
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-2) });

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Should().Be(0);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
