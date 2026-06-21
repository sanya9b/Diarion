using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class DiaryDetailViewModelTests
{
    private readonly Mock<IDiaryService> _diaryServiceMock;
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly DiaryDetailViewModel _viewModel;

    public DiaryDetailViewModelTests()
    {
        _diaryServiceMock = new Mock<IDiaryService>();
        _todoServiceMock = new Mock<ITodoService>();
        
        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());

        _viewModel = new DiaryDetailViewModel(_diaryServiceMock.Object, _todoServiceMock.Object);
    }

    [Fact]
    public void IsExistingEntry_WhenEntryIdIsEmpty_ReturnsFalse()
    {
        // Act
        _viewModel.EntryId = string.Empty;

        // Assert
        _viewModel.IsExistingEntry.Should().BeFalse();
    }

    [Fact]
    public void IsExistingEntry_WhenEntryIdIsNotEmpty_ReturnsTrue()
    {
        // Act
        _viewModel.EntryId = Guid.NewGuid().ToString();

        // Assert
        _viewModel.IsExistingEntry.Should().BeTrue();
    }

    [Fact]
    public async Task AddTodoAsync_WhenDescriptionIsEmpty_DoesNotSave()
    {
        // Arrange
        _viewModel.NewTodoDescription = "  ";

        // Act
        await _viewModel.AddTodoAsync();

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
    }

    [Fact]
    public async Task AddTodoAsync_WhenNotExistingEntry_DoesNotSave()
    {
        // Arrange
        _viewModel.NewTodoDescription = "Test Task";
        _viewModel.EntryId = string.Empty;

        // Act
        await _viewModel.AddTodoAsync();

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
    }

    [Fact]
    public async Task AddTodoAsync_WhenValid_SavesTodoAndClearsDescription()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        _diaryServiceMock
            .Setup(s => s.GetEntryByIdAsync(entryId))
            .ReturnsAsync(new DiaryEntry { Id = entryId });
            
        _viewModel.NewTodoDescription = "Test Task";
        _viewModel.EntryId = entryId.ToString(); // Makes IsExistingEntry = true
        
        // Wait for async load to finish
        await Task.Delay(50);

        // Act
        await _viewModel.AddTodoAsync();

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "Test Task")), Times.Once);
        _viewModel.NewTodoDescription.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadEntryAsync_WhenIdProvided_LoadsEntry()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var mockEntry = new DiaryEntry 
        { 
            Id = entryId, 
            Title = "Test Title", 
            Content = "Test Content", 
            Emotion = Emotion.Happy 
        };
        
        _diaryServiceMock
            .Setup(s => s.GetEntryByIdAsync(entryId))
            .ReturnsAsync(mockEntry);

        // Act
        _viewModel.EntryId = entryId.ToString(); // This triggers OnEntryIdChanged

        // Allow task to finish
        await Task.Delay(100);

        // Assert
        _viewModel.EntryTitle.Should().Be("Test Title");
        _viewModel.EntryContent.Should().Be("Test Content");
        _viewModel.SelectedEmotion.Should().Be(Emotion.Happy);
    }
}