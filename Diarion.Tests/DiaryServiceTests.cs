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
        _diaryService = new DiaryService();
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

        // Cleanup
        await _diaryService.DeleteEntryAsync(entry.Id);
    }

    public void Dispose()
    {
        _diaryService.Dispose();
    }
}
