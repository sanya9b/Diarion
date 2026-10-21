using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services.Search;

/// <summary>
/// The text a person typed into a day, field by field, for search to match against.
/// </summary>
/// <remarks>
/// Only typed text goes in. Ratings, timestamps, habit ticks and mood are numbers the statistics already
/// read; folded in as words they would let a search for a feeling match on a sleep score. Kept from the
/// on-device indexer that used to feed the same fields to the encoder.
/// </remarks>
public static class EntryText
{
    /// <summary>
    /// Free-text fields of a diary entry, in reading order, blanks dropped. The five meal fields are one
    /// segment: one thought split across five boxes.
    /// </summary>
    public static IReadOnlyList<string> Segments(DiaryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Clean(new[]
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
        });
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
