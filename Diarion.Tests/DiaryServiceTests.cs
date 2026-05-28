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

    public void Dispose()
    {
        _diaryService.Dispose();
    }
}
