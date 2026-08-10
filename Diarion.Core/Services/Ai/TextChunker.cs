using System.Collections.Generic;
using System.Linq;

namespace Diarion.Services.Ai;

/// <summary>
/// Splits user text into overlapping windows small enough for the encoder's 512-token limit.
/// </summary>
/// <remarks>
/// Chunks never span two segments. A diary entry is a set of unrelated fields — sleep notes,
/// gratitude, what went wrong — and a window straddling two of them embeds a sentence pair the user
/// never wrote, which then surfaces as a confident and puzzling search hit.
/// </remarks>
public static class TextChunker
{
    public const int DefaultTargetWords = 200;
    public const int DefaultOverlapWords = 40;

    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r', '\f', '\v' };

    /// <summary>
    /// Chunks each segment independently and returns them in order. Blank segments are skipped, so
    /// callers can pass every field of an entry without filtering first.
    /// </summary>
    public static IReadOnlyList<string> Chunk(
        IEnumerable<string?> segments,
        int targetWords = DefaultTargetWords,
        int overlapWords = DefaultOverlapWords)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetWords, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapWords);

        if (overlapWords >= targetWords)
        {
            // Equal or larger overlap means the window never advances.
            throw new ArgumentOutOfRangeException(
                nameof(overlapWords),
                overlapWords,
                $"Overlap must be smaller than the target of {targetWords} words, otherwise chunking cannot terminate.");
        }

        var chunks = new List<string>();
        foreach (var segment in segments)
        {
            AppendSegment(chunks, segment, targetWords, overlapWords);
        }

        return chunks;
    }

    /// <summary>Chunks a single block of text.</summary>
    public static IReadOnlyList<string> ChunkText(
        string? text,
        int targetWords = DefaultTargetWords,
        int overlapWords = DefaultOverlapWords) =>
        Chunk(new[] { text }, targetWords, overlapWords);

    private static void AppendSegment(List<string> chunks, string? segment, int targetWords, int overlapWords)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return;
        }

        var words = segment.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return;
        }

        if (words.Length <= targetWords)
        {
            chunks.Add(string.Join(' ', words));
            return;
        }

        var stride = targetWords - overlapWords;
        for (var start = 0; start < words.Length; start += stride)
        {
            var length = Math.Min(targetWords, words.Length - start);
            chunks.Add(string.Join(' ', words, start, length));

            // The final window is short by design; stopping here avoids emitting a trailing chunk
            // that is nothing but overlap already covered by its predecessor.
            if (start + length >= words.Length)
            {
                break;
            }
        }
    }
}
