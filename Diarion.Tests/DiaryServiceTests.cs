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
        _diaryService = new DiaryService(_dbContext, _todoService, _profileService);
    }

    /// <summary>Forgiveness is on by default for real users, so streak tests state the quota they mean.
    /// Must not run from the constructor: it materialises a profile row, and the cycle-normalisation
    /// tests assert against a collection they expect to populate themselves.</summary>
    private async Task SetStreakGrace(int graceDays)
    {
        var profile = await _profileService.GetUserProfileAsync();
        profile.IsForgivingStreaksEnabled = graceDays > 0;
        profile.StreakGraceDays = graceDays;
        await _profileService.SaveUserProfileAsync(profile);
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
    public async Task GetTodosForDateAsync_WhenRepeatTurnedOff_ShouldNotGenerateFutureRepeats()
    {
        await ClearDatabaseAsync();

        // A daily-repeat task created 10 days ago, viewed on the next 3 days (clones generated).
        var day0 = DateTime.Today.AddDays(-10);
        await _todoService.SaveTodoAsync(new TodoItem
        {
            TaskDescription = "Drink water",
            TargetDate = day0,
            IsDailyRepeat = true
        });
        for (int i = 1; i <= 3; i++)
            await _todoService.GetTodosForDateAsync(day0.AddDays(i));

        // On day 3 the user unchecks "repeat".
        var day3 = (await _todoService.GetTodosForDateAsync(day0.AddDays(3)))
            .Single(t => t.TaskDescription == "Drink water");
        day3.IsDailyRepeat = false;
        await _todoService.SaveTodoAsync(day3);

        // Days after the uncheck day must not have the task any more.
        for (int i = 4; i <= 6; i++)
        {
            var todos = await _todoService.GetTodosForDateAsync(day0.AddDays(i));
            todos.Should().BeEmpty($"repeat was turned off on day 3, so day {i} must be empty");
        }
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenRepeatTurnedOff_ShouldNotAutoMigrateToToday()
    {
        await ClearDatabaseAsync();

        // Daily-repeat task created 3 days ago; clones generated for the in-between days.
        var today = DateTime.Today;
        await _todoService.SaveTodoAsync(new TodoItem
        {
            TaskDescription = "Drink water",
            TargetDate = today.AddDays(-3),
            IsDailyRepeat = true
        });
        await _todoService.GetTodosForDateAsync(today.AddDays(-2));
        var yesterday = await _todoService.GetTodosForDateAsync(today.AddDays(-1));

        // Yesterday the user unchecks "repeat".
        var task = yesterday.Single(t => t.TaskDescription == "Drink water");
        task.IsDailyRepeat = false;
        await _todoService.SaveTodoAsync(task);

        // Opening today must NOT drag the (now non-repeat) task forward via auto-migration.
        var todayTodos = await _todoService.GetTodosForDateAsync(today);
        todayTodos.Should().BeEmpty("a repeat instance that was turned off must stay on its own day");

        // And it must still be present on the day it was unchecked.
        var yesterdayTodos = await _todoService.GetTodosForDateAsync(today.AddDays(-1));
        yesterdayTodos.Should().ContainSingle(t => t.TaskDescription == "Drink water");
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenRepeatTurnedOff_DoesNotDuplicateOnSameDay_Legacy()
    {
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-10);

        // Legacy-style repeating task with NO RepeatGroupId (the case that duplicated).
        _dbContext.GetCollection<TodoItem>(DatabaseConstants.TodosCollection).Insert(new TodoItem
        {
            TaskDescription = "Stretch",
            TargetDate = day0,
            IsDailyRepeat = true
        });

        await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        var day2 = (await _todoService.GetTodosForDateAsync(day0.AddDays(2))).Single(t => t.TaskDescription == "Stretch");

        // Turn the repeat off on day 2.
        day2.IsDailyRepeat = false;
        await _todoService.SaveTodoAsync(day2);

        // Re-open day 2: exactly one task, and it is no longer a repeat.
        var reopened = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        reopened.Count(t => t.TaskDescription == "Stretch").Should().Be(1);
        reopened.Single(t => t.TaskDescription == "Stretch").IsDailyRepeat.Should().BeFalse();
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenRepeatTurnedOff_DoesNotDuplicateOnSameDay_WithGroupId()
    {
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-10);

        await _todoService.SaveTodoAsync(new TodoItem
        {
            TaskDescription = "Stretch",
            TargetDate = day0,
            IsDailyRepeat = true
        });

        await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        var day2 = (await _todoService.GetTodosForDateAsync(day0.AddDays(2))).Single(t => t.TaskDescription == "Stretch");

        day2.IsDailyRepeat = false;
        await _todoService.SaveTodoAsync(day2);

        var reopened = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        reopened.Count(t => t.TaskDescription == "Stretch").Should().Be(1);
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenTurnedOffTaskDeleted_DoesNotRegenerate()
    {
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-10);

        _dbContext.GetCollection<TodoItem>(DatabaseConstants.TodosCollection).Insert(new TodoItem
        {
            TaskDescription = "Stretch",
            TargetDate = day0,
            IsDailyRepeat = true
        });

        await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        var day2 = (await _todoService.GetTodosForDateAsync(day0.AddDays(2))).Single(t => t.TaskDescription == "Stretch");
        day2.IsDailyRepeat = false;
        await _todoService.SaveTodoAsync(day2);

        // Delete the turned-off instance; it must not come back.
        await _todoService.DeleteTodoAsync(day2.Id);

        var reopened = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        reopened.Should().NotContain(t => t.TaskDescription == "Stretch");
    }

    /// <summary>A day only counts towards the streak if the user put something in it.</summary>
    private static DiaryEntry JournaledOn(DateTime date) =>
        new() { Date = date, Emotion = Emotion.Calm };

    [Fact]
    public async Task GetCurrentStreak_WithConsecutiveDays_ReturnsCorrectStreak()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-1)));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(3);
    }

    [Fact]
    public async Task GetCurrentStreak_WithGap_ReturnsCorrectStreakBeforeGap()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentStreak_WhenLastEntryWasYesterday_ReturnsCorrectStreak()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-1)));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));

        var allEntries = await _diaryService.GetAllEntriesAsync();
        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(2, $"Entries in DB: {allEntries.Count}. First Date: {(allEntries.Count > 0 ? allEntries[0].Date.ToString() : "none")}");
    }

    [Fact]
    public async Task GetCurrentStreak_WhenLastEntryWasOlderThanYesterday_ReturnsZero()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));

        // The streak really is broken. It used to report 1 ("opening the app means day 1"), which made
        // IsStreakVisible => CurrentStreak > 0 permanently true in the ViewModels.
        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentStreak_EmptyDatabase_ReturnsZero()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentStreak_IgnoresRowsCreatedByMerelyBrowsingADay()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        // What the day screen persists when cycle tracking is on and the user types nothing.
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today, CycleDay = "14" });
        await _diaryService.SaveEntryAsync(new DiaryEntry { Date = today.AddDays(-1), CycleDay = "13" });

        var streak = await _diaryService.GetCurrentStreakAsync();
        streak.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentStreak_WithGrace_ForgivesSingleMissedDay()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(1);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-3)));

        var streak = await _diaryService.GetCurrentStreakAsync();

        streak.Length.Should().Be(3);
        streak.HeldByGrace.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentStreak_GraceDisabled_MatchesLegacyBehaviour()
    {
        await ClearDatabaseAsync();
        await SetStreakGrace(0);
        var today = DateTime.Today;

        await _diaryService.SaveEntryAsync(JournaledOn(today));
        await _diaryService.SaveEntryAsync(JournaledOn(today.AddDays(-2)));

        var streak = await _diaryService.GetCurrentStreakAsync();

        streak.Length.Should().Be(1);
        streak.HeldByGrace.Should().BeFalse();
    }

    [Fact]
    public async Task GetTodoStatsSummary_CountsTotalAndCompletedInRange()
    {
        await ClearDatabaseAsync();
        var today = DateTime.Today;

        var todos = _dbContext.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        todos.Insert(new TodoItem { TargetDate = today, IsCompleted = true });
        todos.Insert(new TodoItem { TargetDate = today.AddDays(-1), IsCompleted = true });
        todos.Insert(new TodoItem { TargetDate = today.AddDays(-1), IsCompleted = false });
        todos.Insert(new TodoItem { TargetDate = today.AddDays(-10), IsCompleted = true }); // out of range

        var summary = await _todoService.GetTodoStatsSummaryAsync(today.AddDays(-2), today.AddDays(1));

        summary.TotalCount.Should().Be(3);
        summary.CompletedCount.Should().Be(2);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
