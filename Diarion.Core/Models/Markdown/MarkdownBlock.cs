namespace Diarion.Models.Markdown;

/// <summary>
/// One block of a note as the editor draws it: the marker has already been taken off the text, and
/// is redrawn as a real bullet, number or tick box instead.
/// </summary>
public class MarkdownBlock
{
    public MarkdownBlockKind Kind { get; set; } = MarkdownBlockKind.Paragraph;

    /// <summary>
    /// The line with its marker stripped — exactly what the user sees and edits. A
    /// <see cref="MarkdownBlockKind.Paragraph"/> may hold several lines separated by <c>\n</c>;
    /// every other kind is a single line.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Only meaningful for <see cref="MarkdownBlockKind.Checklist"/>.</summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// 1-based position in its run of consecutive numbered items; 0 for every other kind. Assigned
    /// by <c>Renumber</c> rather than read from the text, so a list stays numbered 1,2,3 however it
    /// was typed or however items were inserted.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// The leading whitespace the marker was found behind, kept verbatim. Nothing in the app can
    /// produce it — there is no way to indent a list here yet — but a note written elsewhere can,
    /// and a round trip through the editor must not quietly flatten it.
    /// </summary>
    public string Indent { get; set; } = string.Empty;
}
