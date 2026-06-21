using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class StatisticsServiceTests
{
    [Fact]
    public async Task GetSleepStatisticsAsync_ShouldCalculateCorrectly()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            new DiaryEntryStatsDto { Date = today.AddDays(-2), SleepStart = new TimeSpan(23, 0, 0), SleepEnd = new TimeSpan(7, 0, 0), SleepQuality = 8 },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), SleepStart = new TimeSpan(0, 0, 0), SleepEnd = new TimeSpan(6, 0, 0), SleepQuality = 6 }, // 6 hours
            new DiaryEntryStatsDto { Date = today, SleepStart = null, SleepEnd = null, SleepQuality = 0 } // No sleep data
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);
        var mockTodoService = new Mock<ITodoService>();

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object);

        // Act
        var result = await statsService.GetSleepStatisticsAsync(7);

        // Assert
        result.AverageSleepQuality.Should().Be(7.0); // (8+6)/2
        result.AverageSleepDuration.TotalHours.Should().Be(7.0); // (8h + 6h) / 2
        
        // 8 points for 7 days past + today = 8 points total
        result.DailyData.Should().HaveCount(8);
        result.DailyData.FirstOrDefault(d => d.Date == today.AddDays(-2))?.Duration.TotalHours.Should().Be(8);
    }

    [Fact]
    public async Task GetMoodStatisticsAsync_ShouldCalculateTopEmotion()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<DiaryEntryStatsDto>
        {
            new DiaryEntryStatsDto { Date = today.AddDays(-2), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today.AddDays(-1), Emotion = Emotion.Happy },
            new DiaryEntryStatsDto { Date = today, Emotion = Emotion.Sad }
        };

        mockDiaryService.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);
        var mockTodoService = new Mock<ITodoService>();

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object);

        // Act
        var result = await statsService.GetMoodStatisticsAsync(7);

        // Assert
        result.TopEmotion.Should().Be(Emotion.Happy);
        result.EmotionCounts[Emotion.Happy].Should().Be(2);
        result.EmotionCounts[Emotion.Sad].Should().Be(1);
    }

    [Fact]
    public async Task GetTodoStatisticsAsync_ShouldCalculateCorrectly()
    {
        // Arrange
        var mockDiaryService = new Mock<IDiaryService>();
        var today = DateTime.Today;

        var mockData = new List<TodoStatsDto>
        {
            new TodoStatsDto { TargetDate = today.AddDays(-1), IsCompleted = true },
            new TodoStatsDto { TargetDate = today.AddDays(-1), IsCompleted = false },
            new TodoStatsDto { TargetDate = today, IsCompleted = true }
        };

        var mockTodoService = new Mock<ITodoService>();
        mockTodoService.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockData);

        var statsService = new StatisticsService(mockDiaryService.Object, mockTodoService.Object);

        // Act
        var result = await statsService.GetTodoStatisticsAsync(7);

        // Assert
        result.TotalCount.Should().Be(3);
        result.CompletedCount.Should().Be(2);
        result.CompletionPercentage.Should().BeApproximately(0.666, 0.01);
    }
}