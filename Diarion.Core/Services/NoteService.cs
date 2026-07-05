using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class NoteService : INoteService
{
    private readonly IDatabaseContext _dbContext;

    public NoteService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<Note> GetNotesCollection()
    {
        return _dbContext.GetCollection<Note>("Notes");
    }

    public Task<List<Note>> GetAllNotesAsync()
    {
        var col = GetNotesCollection();
        var notes = col.FindAll().OrderByDescending(n => n.UpdatedAt).ToList();
        return Task.FromResult(notes);
    }

    public Task<List<Note>> SearchNotesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetAllNotesAsync();
        }

        var col = GetNotesCollection();
        var lowerQuery = query.ToLower();
        var notes = col.Find(n => n.Title.ToLower().Contains(lowerQuery) || n.Content.ToLower().Contains(lowerQuery))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();
            
        return Task.FromResult(notes);
    }

    public Task<Note?> GetNoteAsync(string id)
    {
        var col = GetNotesCollection();
        try
        {
            var objectId = new ObjectId(id);
            var note = col.FindById(objectId);
            return Task.FromResult<Note?>(note);
        }
        catch
        {
            return Task.FromResult<Note?>(null);
        }
    }

    public Task SaveNoteAsync(Note note)
    {
        var col = GetNotesCollection();
        note.UpdatedAt = DateTime.Now;
        
        col.Upsert(note);
        
        return Task.CompletedTask;
    }

    public Task DeleteNoteAsync(string id)
    {
        var col = GetNotesCollection();
        try
        {
            var objectId = new ObjectId(id);
            col.Delete(objectId);
        }
        catch
        {
            // Invalid ObjectId, nothing to delete
        }
        
        return Task.CompletedTask;
    }
}
