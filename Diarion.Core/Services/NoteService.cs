using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Ai;
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
        return _dbContext.GetCollection<Note>(DatabaseConstants.NotesCollection);
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

        // Multi-term AND across title + content + tags. The local note set is small, so filtering
        // in memory is simpler and more capable than LiteDB's expression support for tokenized search.
        var terms = query.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var col = GetNotesCollection();
        var notes = col.FindAll()
            .Where(n => terms.All(term => NoteMatchesTerm(n, term)))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();

        return Task.FromResult(notes);
    }

    private static bool NoteMatchesTerm(Note n, string term)
    {
        return (n.Title?.ToLowerInvariant().Contains(term) ?? false)
            || (n.Content?.ToLowerInvariant().Contains(term) ?? false)
            || (n.Tags?.Any(t => t.Contains(term)) ?? false);
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

        // Derive a title from the first line when one wasn't set (service-created notes, e.g. the
        // create-on-click backlink flow), so title-based linking/backlinks resolve consistently.
        if (string.IsNullOrWhiteSpace(note.Title))
        {
            note.Title = DeriveTitle(note.Content);
        }

        // Denormalize tags and outgoing links from the body so queries stay cheap.
        note.Tags = NoteParser.ExtractTags(note.Content);
        note.LinkedTitles = NoteParser.ExtractLinks(note.Content);

        col.Upsert(note);
        Announce(note.Id.ToString());

        return Task.CompletedTask;
    }

    private static string DeriveTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Untitled";

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "Untitled";

        var first = lines[0].Trim();
        return first.Length > 40 ? first.Substring(0, 40) + "..." : first;
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

        Announce(id);

        return Task.CompletedTask;
    }

    private static void Announce(string id) =>
        WeakReferenceMessenger.Default.Send(
            new DocumentChangedMessage(EmbeddingSourceKind.Note, id));

    public Task<List<Note>> GetBacklinksAsync(string title, string? excludeId = null)
    {
        var key = NoteParser.NormalizeTitle(title);
        if (key.Length == 0) return Task.FromResult(new List<Note>());

        var col = GetNotesCollection();
        var notes = col.FindAll()
            .Where(n => n.LinkedTitles != null
                        && n.LinkedTitles.Contains(key)
                        && (excludeId == null || n.Id.ToString() != excludeId))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();

        return Task.FromResult(notes);
    }

    public Task<Note?> GetNoteByTitleAsync(string title)
    {
        var key = NoteParser.NormalizeTitle(title);
        if (key.Length == 0) return Task.FromResult<Note?>(null);

        var col = GetNotesCollection();
        var note = col.FindAll()
            .Where(n => NoteParser.NormalizeTitle(n.Title) == key)
            .OrderByDescending(n => n.UpdatedAt)
            .FirstOrDefault();

        return Task.FromResult<Note?>(note);
    }

    public Task<List<string>> GetAllTagsAsync()
    {
        var col = GetNotesCollection();
        var tags = col.FindAll()
            .Where(n => n.Tags != null)
            .SelectMany(n => n.Tags)
            .Distinct()
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(tags);
    }

    public Task<List<Note>> GetNotesByTagAsync(string tag)
    {
        var key = (tag ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0) return GetAllNotesAsync();

        var col = GetNotesCollection();
        var notes = col.FindAll()
            .Where(n => n.Tags != null && n.Tags.Contains(key))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();

        return Task.FromResult(notes);
    }
}
