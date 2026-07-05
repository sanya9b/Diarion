using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface INoteService
{
    Task<List<Note>> GetAllNotesAsync();
    Task<List<Note>> SearchNotesAsync(string query);
    Task<Note?> GetNoteAsync(string id);
    Task SaveNoteAsync(Note note);
    Task DeleteNoteAsync(string id);
}
