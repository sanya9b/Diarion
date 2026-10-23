using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The home screen's note field: it files into Notes, never into the day.</summary>
public class QuickNoteViewModelTests
{
    private readonly Mock<INoteService> _notes = new();

    private QuickNoteViewModel Create() => new(_notes.Object);

    [Fact]
    public async Task Sending_files_a_trimmed_inbox_note_and_empties_the_field()
    {
        Note? saved = null;
        _notes.Setup(n => n.SaveNoteAsync(It.IsAny<Note>())).Callback<Note>(n => saved = n).Returns(Task.CompletedTask);
        var vm = Create();
        vm.Text = "  купити батарейки  ";

        await vm.SendCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.Content.Should().Be("купити батарейки");
        saved.IsInInbox.Should().BeTrue();
        vm.Text.Should().BeEmpty();
        vm.JustSaved.Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_field_sends_nothing()
    {
        var vm = Create();
        vm.Text = "   ";

        vm.CanSend.Should().BeFalse();
        await vm.SendCommand.ExecuteAsync(null);

        _notes.Verify(n => n.SaveNoteAsync(It.IsAny<Note>()), Times.Never);
        vm.JustSaved.Should().BeFalse();
    }

    [Fact]
    public async Task Typing_again_clears_the_saved_sign()
    {
        _notes.Setup(n => n.SaveNoteAsync(It.IsAny<Note>())).Returns(Task.CompletedTask);
        var vm = Create();
        vm.Text = "перша";
        await vm.SendCommand.ExecuteAsync(null);

        vm.Text = "д";

        vm.JustSaved.Should().BeFalse();
        vm.CanSend.Should().BeTrue();
    }

    [Fact]
    public async Task A_read_only_diary_keeps_the_text_so_nothing_is_lost()
    {
        _notes.Setup(n => n.SaveNoteAsync(It.IsAny<Note>())).ThrowsAsync(new ReadOnlyAccessException());
        var vm = Create();
        vm.Text = "думка";

        await vm.SendCommand.ExecuteAsync(null);

        vm.Text.Should().Be("думка");
        vm.JustSaved.Should().BeFalse();
        vm.IsSending.Should().BeFalse();
    }
}
