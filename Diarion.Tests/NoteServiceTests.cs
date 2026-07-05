using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class NoteServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly NoteService _noteService;

    public NoteServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _noteService = new NoteService(_dbContext);
    }

    [Fact]
    public async Task SaveNoteAsync_ShouldSaveNewNote()
    {
        // Arrange
        var note = new Note
        {
            Title = "Test Note",
            Content = "Test Content",
            ColorTheme = "Theme_Amber"
        };

        // Act
        await _noteService.SaveNoteAsync(note);
        var fetchedNote = await _noteService.GetNoteAsync(note.Id.ToString());

        // Assert
        fetchedNote.Should().NotBeNull();
        fetchedNote!.Title.Should().Be("Test Note");
        fetchedNote.Content.Should().Be("Test Content");
        
        // Cleanup
        await _noteService.DeleteNoteAsync(note.Id.ToString());
    }
    
    [Fact]
    public async Task SearchNotesAsync_ShouldReturnMatchingNotes()
    {
        // Arrange
        var note1 = new Note { Title = "Grocery List", Content = "Milk, Bread" };
        var note2 = new Note { Title = "Ideas", Content = "App features: Notes" };
        var note3 = new Note { Title = "Random", Content = "Nothing here" };

        await _noteService.SaveNoteAsync(note1);
        await _noteService.SaveNoteAsync(note2);
        await _noteService.SaveNoteAsync(note3);

        // Act
        var results = await _noteService.SearchNotesAsync("list");
        var results2 = await _noteService.SearchNotesAsync("App features");

        // Assert
        results.Should().ContainSingle(n => n.Title == "Grocery List");
        results2.Should().ContainSingle(n => n.Title == "Ideas");

        // Cleanup
        await _noteService.DeleteNoteAsync(note1.Id.ToString());
        await _noteService.DeleteNoteAsync(note2.Id.ToString());
        await _noteService.DeleteNoteAsync(note3.Id.ToString());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
