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

    [Fact]
    public async Task Initialize_BuildsTagFilters_AndSelectingTagFiltersNotes()
    {
        var notes = new List<Note>
        {
            new Note { Title = "A", Tags = new List<string> { "work" } },
            new Note { Title = "B", Tags = new List<string> { "home" } }
        };
        _mockNoteService.Setup(s => s.GetAllNotesAsync()).ReturnsAsync(notes);
        _mockNoteService.Setup(s => s.SearchNotesAsync(It.IsAny<string>())).ReturnsAsync(notes);

        await _viewModel.InitializeCommand.ExecuteAsync(null);

        _viewModel.ShowFilters.Should().BeTrue();
        _viewModel.Filters.Should().HaveCount(3); // All, #home, #work (tags sorted)

        var workChip = _viewModel.Filters.First(f => f.Tag == "work");
        await _viewModel.SelectFilterCommand.ExecuteAsync(workChip);

        _viewModel.Notes.Should().ContainSingle(n => n.Title == "A");
        workChip.IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task QuickCapture_SavesInboxNote_ClearsText_AndShowsInboxChip()
    {
        var saved = new List<Note>();
        _mockNoteService.Setup(s => s.SaveNoteAsync(It.IsAny<Note>()))
            .Returns<Note>(n => { saved.Add(n); return Task.CompletedTask; });
        _mockNoteService.Setup(s => s.GetAllNotesAsync()).ReturnsAsync(() => saved);
        _mockNoteService.Setup(s => s.SearchNotesAsync(It.IsAny<string>())).ReturnsAsync(() => saved);

        _viewModel.QuickCaptureText = "buy milk";
        await _viewModel.QuickCaptureCommand.ExecuteAsync(null);

        saved.Should().ContainSingle(n => n.Content == "buy milk" && n.IsInInbox);
        _viewModel.QuickCaptureText.Should().BeEmpty();
        _viewModel.Filters.Should().Contain(f => f.Kind == NoteFilterKind.Inbox);
    }

    [Fact]
    public async Task QuickCapture_WhitespaceOnly_DoesNothing()
    {
        _viewModel.QuickCaptureText = "   ";
        await _viewModel.QuickCaptureCommand.ExecuteAsync(null);

        _mockNoteService.Verify(s => s.SaveNoteAsync(It.IsAny<Note>()), Times.Never);
    }
}
