namespace Diarion.Models.Markdown;

/// <summary>
/// What one line of a note has been recognised as.
/// </summary>
/// <remarks>
/// Never persisted — a note is stored as markdown text and re-recognised every time it is opened —
/// so these members can be renamed or reordered without a migration. That is the whole point of
/// keeping the markdown as the single source of truth: the blocks are a view of the text, not a
/// second copy of it that could drift.
/// </remarks>
public enum MarkdownBlockKind
{
    /// <summary>Prose. The only kind that may span several lines: consecutive plain lines are held
    /// as one run so that typing, wrapping and deleting inside a paragraph stay native.</summary>
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Bullet,
    Numbered,
    /// <summary>A tickable item — <c>- [ ]</c> / <c>- [x]</c>.</summary>
    Checklist,
    Quote
}
