using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class NotesViewModelTests
{
    private readonly Mock<INoteService> _mockNoteService;
    private readonly Mock<INavigationService> _mockNavigationService;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly NotesViewModel _viewModel;

    public NotesViewModelTests()
    {
        _mockNoteService = new Mock<INoteService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockDialogService = new Mock<IDialogService>();

        _viewModel = new NotesViewModel(_mockNoteService.Object, _mockNavigationService.Object, _mockDialogService.Object);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadNotes()
    {
        // Arrange
        var notesList = new List<Note>
        {
            new Note { Title = "Note 1" },
            new Note { Title = "Note 2" }
        };
        
        _mockNoteService.Setup(s => s.SearchNotesAsync(It.IsAny<string>()))
            .ReturnsAsync(notesList);

        // Act
        await _viewModel.InitializeCommand.ExecuteAsync(null);

        // Assert
        _viewModel.Notes.Should().HaveCount(2);
        _viewModel.Notes[0].Title.Should().Be("Note 1");
    }

    [Fact]
    public async Task PerformSearchCommand_ShouldFilterNotes()
    {
        // Arrange
        var filteredList = new List<Note>
        {
            new Note { Title = "Target Note" }
        };
        
        _mockNoteService.Setup(s => s.SearchNotesAsync("Target"))
            .ReturnsAsync(filteredList);

        // Act
        _viewModel.SearchQuery = "Target";
        // OnSearchQueryChanged will be called automatically and triggers PerformSearchAsync

        // Give it a tiny bit of time as OnSearchQueryChanged uses discard `_ =`
        await Task.Delay(50); 

        // Assert
        _viewModel.Notes.Should().HaveCount(1);
        _viewModel.Notes[0].Title.Should().Be("Target Note");
    }
}
