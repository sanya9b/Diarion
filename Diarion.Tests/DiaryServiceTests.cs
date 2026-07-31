using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Moq;
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
        // Rules too, or a "cleared" database would quietly start producing tasks again on the next read.
        _dbContext.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection).DeleteAll();
    }

    /// <summary>Saves a task and puts it on a daily rule anchored to its own day.</summary>
    private async Task<TodoItem> StartDailySeriesAsync(
        string description, DateTime date, TodoPriority priority = TodoPriority.Medium)
    {
        var todo = new TodoItem { TaskDescription = description, TargetDate = date, Priority = priority };
        await _todoService.SaveTodoAsync(todo);
        await _todoService.SetRecurrenceAsync(
            todo.Id, new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = date });
        return todo;
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
        await StartDailySeriesAsync("Repeating High Priority Task", yesterday, TodoPriority.High);

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

        // A daily-repeat task created 10 days ago, viewed on the next 3 days (occurrences materialized).
        var day0 = DateTime.Today.AddDays(-10);
        await StartDailySeriesAsync("Drink water", day0);
        for (int i = 1; i <= 3; i++)
            await _todoService.GetTodosForDateAsync(day0.AddDays(i));

        // On day 3 the user unchecks "repeat".
        var day3 = (await _todoService.GetTodosForDateAsync(day0.AddDays(3)))
            .Single(t => t.TaskDescription == "Drink water");
        await _todoService.SetRecurrenceAsync(day3.Id, null);

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

        // Daily-repeat task created 3 days ago; occurrences materialized for the in-between days.
        var today = DateTime.Today;
        await StartDailySeriesAsync("Drink water", today.AddDays(-3));
        await _todoService.GetTodosForDateAsync(today.AddDays(-2));
        var yesterday = await _todoService.GetTodosForDateAsync(today.AddDays(-1));

        // Yesterday the user unchecks "repeat".
        var task = yesterday.Single(t => t.TaskDescription == "Drink water");
        await _todoService.SetRecurrenceAsync(task.Id, null);

        // Opening today must NOT drag the ended occurrence forward via auto-migration. It is pinned by
        // its own provenance now, where the old scheme needed the end date to do double duty.
        var todayTodos = await _todoService.GetTodosForDateAsync(today);
        todayTodos.Should().BeEmpty("a repeat instance that was turned off must stay on its own day");

        // And it must still be present on the day it was unchecked.
        var yesterdayTodos = await _todoService.GetTodosForDateAsync(today.AddDays(-1));
        yesterdayTodos.Should().ContainSingle(t => t.TaskDescription == "Drink water");
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenRepeatTurnedOff_LeavesOneRowAndEndsTheSeries()
    {
        // The two tests this replaces both existed because the grouping key was ambiguous: one covered a
        // series with a group id, one a series without. A series without an id is now unrepresentable —
        // it is a Guid or it is not a series — so the legacy half moved to MigrationRunnerTests, where
        // that shape still turns up in databases already on disk.
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-10);

        await StartDailySeriesAsync("Stretch", day0);

        await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        var day2 = (await _todoService.GetTodosForDateAsync(day0.AddDays(2))).Single(t => t.TaskDescription == "Stretch");

        await _todoService.SetRecurrenceAsync(day2.Id, null);

        var reopened = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        reopened.Count(t => t.TaskDescription == "Stretch").Should().Be(1);

        var rule = await _todoService.GetRecurringTaskAsync(day2.RecurringTaskId!.Value);
        rule!.Recurrence.EndDate.Should().BeBefore(day2.TargetDate, "the series ended before the day it was switched off on");
    }

    [Fact]
    public async Task GetTodosForDateAsync_WhenTurnedOffTaskDeleted_DoesNotRegenerate()
    {
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-10);

        await StartDailySeriesAsync("Stretch", day0);

        await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        var day2 = (await _todoService.GetTodosForDateAsync(day0.AddDays(2))).Single(t => t.TaskDescription == "Stretch");
        await _todoService.SetRecurrenceAsync(day2.Id, null);

        // Delete the turned-off instance; it must not come back.
        await _todoService.DeleteTodoAsync(day2.Id);

        var reopened = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        reopened.Should().NotContain(t => t.TaskDescription == "Stretch");
    }

    [Fact]
    public async Task GetTodosForDateAsync_TwoSeriesWithTheSameDescriptionDoNotMerge()
    {
        // The defect the whole rewrite is for. Grouped by text, one of these two swallowed the other and
        // the user simply never saw the second task again.
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-5);

        await StartDailySeriesAsync("Прибрати", day0);
        await StartDailySeriesAsync("Прибрати", day0);

        var next = await _todoService.GetTodosForDateAsync(day0.AddDays(1));

        next.Count(t => t.TaskDescription == "Прибрати").Should().Be(2);
    }

    [Fact]
    public async Task SaveTodoAsync_RenamingAnOccurrenceRenamesTheSeries()
    {
        // The old scheme did this too, but only as a side effect of the newest instance being the
        // template. Renaming an older one changed nothing, which was impossible to predict.
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-5);
        await StartDailySeriesAsync("Drink water", day0);

        var day1 = (await _todoService.GetTodosForDateAsync(day0.AddDays(1))).Single();
        day1.TaskDescription = "Drink 2L water";
        await _todoService.SaveTodoAsync(day1);

        var day2 = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        day2.Should().ContainSingle(t => t.TaskDescription == "Drink 2L water");
    }

    [Fact]
    public async Task GetTodosForDateAsync_ADemotedOccurrenceDoesNotDemoteTheSeries()
    {
        // Under the old scheme the demoted clone became the template for the next day, so one busy day
        // dropped a High series to Medium permanently. The template lives on the rule now.
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-5);
        await StartDailySeriesAsync("Big thing", day0, TodoPriority.High);

        // Crowd day 1 so the arriving occurrence is demoted.
        for (int i = 0; i < RecurringTaskPlanner.MaxHighPriorityPerDay; i++)
        {
            await _todoService.SaveTodoAsync(new TodoItem
            {
                TaskDescription = $"Busy {i}",
                TargetDate = day0.AddDays(1),
                Priority = TodoPriority.High
            });
        }

        var day1 = await _todoService.GetTodosForDateAsync(day0.AddDays(1));
        day1.Single(t => t.TaskDescription == "Big thing").Priority.Should().Be(TodoPriority.Medium);

        var day2 = await _todoService.GetTodosForDateAsync(day0.AddDays(2));
        day2.Single(t => t.TaskDescription == "Big thing").Priority.Should().Be(TodoPriority.High);
    }

    [Fact]
    public async Task DeleteTodoAsync_AnOccurrenceOfALiveSeriesStaysDeleted()
    {
        await ClearDatabaseAsync();
        var day0 = DateTime.Today.AddDays(-5);
        await StartDailySeriesAsync("Stretch", day0);

        var day1 = (await _todoService.GetTodosForDateAsync(day0.AddDays(1))).Single();
        await _todoService.DeleteTodoAsync(day1.Id);

        (await _todoService.GetTodosForDateAsync(day0.AddDays(1))).Should().BeEmpty();

        // One day skipped, not the series ended — the difference between deleting a row and unchecking
        // the box.
        (await _todoService.GetTodosForDateAsync(day0.AddDays(2)))
            .Should().ContainSingle(t => t.TaskDescription == "Stretch");
    }

    [Fact]
    public async Task GetTodosForDateAsync_AGeneratedOccurrenceGetsItsReminderScheduled()
    {
        // The old generator inserted straight into the collection, so a repeating task with a reminder
        // notified only on the instances the user had saved through the form by hand.
        var notifications = new Mock<INotificationService>();
        var service = new TodoService(_dbContext, notifications.Object);
        var today = DateTime.Today;

        var todo = new TodoItem
        {
            TaskDescription = "Pills",
            TargetDate = today,
            HasTime = true,
            TargetTime = TimeSpan.FromHours(9),
            HasReminder = true
        };
        await service.SaveTodoAsync(todo);
        await service.SetRecurrenceAsync(todo.Id, new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = today });
        notifications.Invocations.Clear();

        // Tomorrow, so the reminder is still in the future whatever time of day the suite runs at.
        await service.GetTodosForDateAsync(today.AddDays(1));

        notifications.Verify(
            n => n.ScheduleTodoReminder(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()),
            Times.Once);
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
