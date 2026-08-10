using System;
using LiteDB;

namespace Diarion.Models.Ai;

/// <summary>What an <see cref="EmbeddingChunk"/> was derived from.</summary>
public static class EmbeddingSourceKind
{
    public const string Diary = "diary";
    public const string Note = "note";
}

/// <summary>
/// One embedded slice of user text. Rows are derived data: they can always be rebuilt from the
/// diary and notes, which is why they are excluded from export and dropped rather than migrated
/// whenever the vector format changes.
/// </summary>
public class EmbeddingChunk
{
    /// <summary>
    /// Deterministic id derived from source and ordinal, so re-indexing the same slice overwrites
    /// its row instead of accumulating duplicates. See <see cref="BuildId"/>.
    /// </summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>One of the <see cref="EmbeddingSourceKind"/> constants.</summary>
    public string SourceKind { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the source document as a string, because the two sources disagree on type:
    /// <c>DiaryEntry.Id</c> is a <see cref="Guid"/> and <c>Note.Id</c> is an <see cref="ObjectId"/>.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Zero-based position of this chunk within its source.</summary>
    public int Ordinal { get; set; }

    /// <summary>Date the chunk is filed under — the entry's date, or the note's last update.</summary>
    public DateTime SourceDate { get; set; }

    /// <summary>Verbatim chunk text. Doubles as the RAG context block and the search snippet.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the whole source document's normalized text. Staleness is a comparison against this
    /// plus <see cref="ModelId"/> — there is no timestamp to trust and no cursor to lose.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Model that produced <see cref="Vector"/>. A mismatch invalidates the row.</summary>
    public string ModelId { get; set; } = string.Empty;

    public int Dim { get; set; }

    /// <summary>
    /// L2-normalized float32, little-endian. Stored as a blob rather than a <c>float[]</c>: BSON
    /// widens arrays to double, so 384 dimensions would cost roughly 3 KB and a slow mapping pass
    /// instead of 1536 bytes and a block copy.
    /// </summary>
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    public DateTime IndexedAtUtc { get; set; } = DateTime.UtcNow;

    public static string BuildId(string sourceKind, string sourceId, int ordinal) =>
        $"{sourceKind}|{sourceId}|{ordinal}";
}
