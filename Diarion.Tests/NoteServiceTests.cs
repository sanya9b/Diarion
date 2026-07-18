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

    [Fact]
    public async Task SaveNoteAsync_PopulatesTagsAndLinksFromContent()
    {
        var note = new Note { Content = "Planning #project with [[Design Doc]] and #idea" };

        await _noteService.SaveNoteAsync(note);
        var fetched = await _noteService.GetNoteAsync(note.Id.ToString());

        fetched!.Tags.Should().BeEquivalentTo(new[] { "project", "idea" });
        fetched.LinkedTitles.Should().Equal("design doc"); // normalized
    }

    [Fact]
    public async Task GetBacklinksAsync_ReturnsNotesLinkingToTitle_ExcludingSelf()
    {
        var target = new Note { Content = "Design Doc" };                 // Title -> "Design Doc"
        var linker = new Note { Content = "See [[Design Doc]] for specs" };
        var unrelated = new Note { Content = "Nothing relevant here" };

        await _noteService.SaveNoteAsync(target);
        await _noteService.SaveNoteAsync(linker);
        await _noteService.SaveNoteAsync(unrelated);

        var backlinks = await _noteService.GetBacklinksAsync("Design Doc", target.Id.ToString());

        backlinks.Should().ContainSingle(n => n.Id == linker.Id);
    }

    [Fact]
    public async Task GetNoteByTitleAsync_ResolvesCaseInsensitively()
    {
        var note = new Note { Content = "Weekly Review" };
        await _noteService.SaveNoteAsync(note);

        var found = await _noteService.GetNoteByTitleAsync("weekly review");

        found.Should().NotBeNull();
        found!.Id.Should().Be(note.Id);
    }

    [Fact]
    public async Task GetAllTagsAsync_ReturnsDistinctSorted()
    {
        await _noteService.SaveNoteAsync(new Note { Content = "#zeta and #alpha" });
        await _noteService.SaveNoteAsync(new Note { Content = "#alpha again and #mid" });

        var tags = await _noteService.GetAllTagsAsync();

        tags.Should().Equal("alpha", "mid", "zeta");
    }

    [Fact]
    public async Task SearchNotesAsync_MultiTerm_RequiresAllTermsAcrossTitleContentTags()
    {
        await _noteService.SaveNoteAsync(new Note { Content = "Trip planning to Kyiv #travel" });
        await _noteService.SaveNoteAsync(new Note { Content = "Kyiv weather notes" });

        var results = await _noteService.SearchNotesAsync("kyiv travel");

        results.Should().ContainSingle(n => n.Content.Contains("Trip planning"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
