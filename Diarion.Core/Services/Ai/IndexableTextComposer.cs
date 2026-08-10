using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Diarion.Models;

namespace Diarion.Services.Ai;

/// <summary>
/// Decides which of a document's fields are worth embedding, and turns them into segments the
/// chunker will never merge across.
/// </summary>
/// <remarks>
/// Only text the user typed goes in. Ratings, timestamps, habit ticks and mood scalars are already
/// searchable as numbers through statistics, and folding them in as prose would let a query about
/// feelings match on a sleep score.
/// </remarks>
public static class IndexableTextComposer
{
    /// <summary>
    /// ASCII unit separator. The hash separator has to be something the user cannot type: with a
    /// newline, moving a word from the end of one field to the start of the next would leave the
    /// hash unchanged and the document would never be re-indexed.
    /// </summary>
    private const char SegmentSeparator = '\u001F';

    /// <summary>
    /// Free-text fields of a diary entry, in reading order. The five meal fields collapse into one
    /// segment: they are one thought split across five boxes, and separately each would earn its
    /// own vector for two or three words.
    /// </summary>
    public static IReadOnlyList<string> ComposeEntry(DiaryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var segments = new[]
        {
            entry.Title,
            entry.Content,
            entry.PromptAnswer,
            entry.Gratitude,
            entry.Triggers,
            entry.SoulFood,
            entry.SupportForOthers,
            entry.SleepNotes,
            entry.IntimateLife,
            JoinMeals(entry),
        };

        return Clean(segments);
    }

    /// <summary>
    /// Note title and body. Tags are skipped — they are denormalized from the body, so indexing
    /// them would weight a word twice for having a hash in front of it.
    /// </summary>
    public static IReadOnlyList<string> ComposeNote(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return Clean(new[] { note.Title, note.Content });
    }

    /// <summary>
    /// Stable hash of everything that would be indexed. This is the whole staleness mechanism:
    /// a document is out of date when its hash no longer matches the stored one, which needs no
    /// timestamp to be trustworthy and no cursor to be resumable.
    /// </summary>
    public static string ComputeHash(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var joined = string.Join(SegmentSeparator, segments);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> segments) =>
        segments
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();

    private static string JoinMeals(DiaryEntry entry) =>
        string.Join(", ", Clean(new[]
        {
            entry.BreakfastFood,
            entry.SecondBreakfastFood,
            entry.LunchFood,
            entry.SnackFood,
            entry.DinnerFood,
        }));
}
