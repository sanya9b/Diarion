using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Closing a note saves it, because the auto-save debouncer may still be holding the last keystroke.
/// These cover the other half of that: a note that was only read must come back out of the editor
/// exactly as it went in, since a save re-stamps <c>UpdatedAt</c> — the date the notes list shows and
/// the order it is sorted by.
/// </summary>
public class NoteDetailViewModelTests
{
    private readonly Mock<INoteService> _noteService = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly NoteDetailViewModel _viewModel;

    public NoteDetailViewModelTests()
    {
        _viewModel = new NoteDetailViewModel(
            _noteService.Object,
            _navigation.Object,
            _dialogs.Object,
            new NoKeyboardInsetService());
    }

    private Note Open(string content)
    {
        var note = new Note { Title = "Список", Content = content };
        _noteService.Setup(s => s.GetNoteAsync(note.Id.ToString())).ReturnsAsync(note);

        _viewModel.NoteId = note.Id.ToString(); // the query property the page navigates with
        return note;
    }

    [Fact]
    public async Task ClosingAnUneditedNote_DoesNotSaveIt()
    {
        Open("Список\n- молоко\n- хліб");

        await _viewModel.FlushSaveCommand.ExecuteAsync(null);

        _noteService.Verify(s => s.SaveNoteAsync(It.IsAny<Note>()), Times.Never);
    }

    [Fact]
    public async Task ClosingAnUneditedNote_Repeatedly_StillDoesNotSaveIt()
    {
        Open("Список");

        await _viewModel.FlushSaveCommand.ExecuteAsync(null);
        await _viewModel.SaveNoteCommand.ExecuteAsync(null); // the back arrow

        _noteService.Verify(s => s.SaveNoteAsync(It.IsAny<Note>()), Times.Never);
    }

    [Fact]
    public async Task ClosingAnEditedNote_SavesIt()
    {
        var note = Open("Список\n- молоко");

        _viewModel.Blocks[^1].Text = "- молоко і хліб"; // typing into a line, as the page does

        await _viewModel.FlushSaveCommand.ExecuteAsync(null);

        _noteService.Verify(s => s.SaveNoteAsync(note), Times.Once);
        note.Content.Should().Contain("хліб");
    }

    [Fact]
    public async Task AnEditSavedOnce_IsNotSavedAgainOnClose()
    {
        var note = Open("Список");

        _viewModel.NoteContent = "Список покупок";
        await _viewModel.FlushSaveCommand.ExecuteAsync(null);
        await _viewModel.FlushSaveCommand.ExecuteAsync(null);

        _noteService.Verify(s => s.SaveNoteAsync(note), Times.Once);
    }

    [Fact]
    public async Task TickingABox_CountsAsAnEdit()
    {
        var note = Open("- [ ] молоко");

        _viewModel.Blocks[0].ToggleCheckCommand.Execute(null);

        await _viewModel.FlushSaveCommand.ExecuteAsync(null);

        _noteService.Verify(s => s.SaveNoteAsync(note), Times.Once);
        note.Content.Should().Contain("[x]");
    }
}
