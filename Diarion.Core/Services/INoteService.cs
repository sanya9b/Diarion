using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface INoteService
{
    Task<List<Note>> GetAllNotesAsync();

    /// <summary>Multi-term, case-insensitive search across title, content and tags (all terms must match).</summary>
    Task<List<Note>> SearchNotesAsync(string query);

    Task<Note?> GetNoteAsync(string id);
    Task SaveNoteAsync(Note note);
    Task DeleteNoteAsync(string id);

    /// <summary>Notes that link to <paramref name="title"/> via <c>[[Title]]</c> (excluding the note itself).</summary>
    Task<List<Note>> GetBacklinksAsync(string title, string? excludeId = null);

    /// <summary>Resolve a <c>[[link]]</c> target: the first note whose title matches (case-insensitive).</summary>
    Task<Note?> GetNoteByTitleAsync(string title);

    /// <summary>All distinct tags across notes, sorted alphabetically.</summary>
    Task<List<string>> GetAllTagsAsync();

    Task<List<Note>> GetNotesByTagAsync(string tag);
}
