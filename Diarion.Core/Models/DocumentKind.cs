namespace Diarion.Models;

/// <summary>
/// Which kind of document a save, a search hit or a navigation is about. Carried as a string by
/// <c>DocumentChangedMessage</c> and <see cref="SearchHit"/>. It was <c>EmbeddingSourceKind</c> while the
/// on-device encoder indexed these documents; the encoder is gone (2026-10-21), the two kinds are not.
/// </summary>
public static class DocumentKind
{
    public const string Diary = "diary";
    public const string Note = "note";
}
