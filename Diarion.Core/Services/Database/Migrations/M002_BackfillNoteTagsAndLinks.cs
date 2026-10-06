using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Backfills <see cref="Note.Tags"/> and <see cref="Note.LinkedTitles"/> for notes created before
/// tag/backlink parsing existed, by re-parsing each note's content. Idempotent: a note whose stored
/// tags/links already match the parsed result is skipped.
/// </summary>
public sealed class M002_BackfillNoteTagsAndLinks : IMigration
{
    public int ToVersion => 2;

    public void Up(LiteDatabase db)
    {
        var notes = db.GetCollection<Note>(DatabaseConstants.NotesCollection);
        foreach (var note in notes.FindAll())
        {
            var tags = NoteParser.ExtractTags(note.Content);
            var links = NoteParser.ExtractLinks(note.Content);

            if (SameSequence(note.Tags, tags) && SameSequence(note.LinkedTitles, links))
            {
                continue;
            }

            note.Tags = tags;
            note.LinkedTitles = links;
            notes.Update(note);
        }
    }

    private static bool SameSequence(List<string>? current, List<string> parsed)
    {
        if (current == null) return parsed.Count == 0;
        return current.SequenceEqual(parsed);
    }
}
