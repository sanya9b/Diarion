using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class TodoDetailViewModelTests
{
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly TodoDetailViewModel _viewModel;

    public TodoDetailViewModelTests()
    {
        _todoServiceMock = new Mock<ITodoService>();
        _viewModel = new TodoDetailViewModel(_todoServiceMock.Object);
    }

    [Fact]
    public async Task SaveAsync_WhenDescriptionIsEmpty_DoesNotSave()
    {
        // Arrange
        _viewModel.TaskDescription = "  ";

        // Act
        await _viewModel.SaveAsync();

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.IsAny<TodoItem>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_WhenValid_SavesTodo()
    {
        // Arrange
        _viewModel.TaskDescription = "My Task";
        _viewModel.SelectedPriority = TodoPriority.Medium;

        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());

        // Exception will be thrown inside SaveAsync at Shell.Current.GoToAsync("..") 
        // since Shell.Current is null in test environment. 
        // We can catch it to verify the logic before it.
        try
        {
            // Act
            await _viewModel.SaveAsync();
        }
        catch (NullReferenceException)
        {
            // Expected because Shell.Current is null
        }

        // Assert
        _todoServiceMock.Verify(s => s.SaveTodoAsync(It.Is<TodoItem>(t => t.TaskDescription == "My Task")), Times.Once);
    }

    [Fact]
    public void SelectPriority_UpdatesSelectedPriorityAndItems()
    {
        // Arrange
        var lowPriorityItem = _viewModel.PrioritiesList[0]; // Low
        var highPriorityItem = _viewModel.PrioritiesList[2]; // High

        // Act
        _viewModel.SelectPriorityCommand.Execute(highPriorityItem);

        // Assert
        _viewModel.SelectedPriority.Should().Be(TodoPriority.High);
        highPriorityItem.IsSelected.Should().BeTrue();
        lowPriorityItem.IsSelected.Should().BeFalse();
    }
}