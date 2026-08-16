using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Diarion.Models.Markdown;

namespace Diarion.Services;

/// <summary>
/// Turns the markdown a note is stored as into the blocks the editor draws, and back again.
/// Pure and UI-free, like its neighbour <see cref="NoteParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// The note stays one markdown string. Blocks are derived on open and thrown away on close, which is
/// why ticking a box needs no schema change, why full-text search and the markdown export keep
/// working untouched, and why a note edited here is still readable as plain text anywhere else.
/// </para>
/// <para>
/// Every marker demands whitespace after it — <c>"# "</c>, <c>"- "</c>, <c>"1. "</c>, <c>"&gt; "</c>.
/// That is not pedantry about the spec: without it <c>#робота</c> would become a heading instead of a
/// tag, and the moment a user typed a lone <c>&gt;</c> the line would jump into a quote under their
/// fingers. The tick box is the one exception — <c>[]</c> is recognised as soon as it is closed,
/// because there is nothing else a line can mean once it starts that way.
/// </para>
/// </remarks>
public static class MarkdownParser
{
    private static readonly Regex HeadingPattern = new(
        @"^([ \t]*)(#{1,3})[ \t]+(.*)$", RegexOptions.Compiled);

    // The list marker is optional so that Bear-style "[] milk" works as well as markdown's "- [ ] milk".
    // Exactly one character between the brackets, which is what keeps "[[Linked note]]" out of here.
    private static readonly Regex ChecklistPattern = new(
        @"^([ \t]*)(?:[-*+][ \t]+)?\[([ xX]?)\][ \t]*(.*)$", RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"^([ \t]*)(\d{1,9})[.)][ \t]+(.*)$", RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"^([ \t]*)[-*+][ \t]+(.*)$", RegexOptions.Compiled);

    private static readonly Regex QuotePattern = new(
        @"^([ \t]*)>[ \t]+(.*)$", RegexOptions.Compiled);

    // Inline emphasis. Longest opener first: "*" tried before "**" would eat one star at a time and
    // leave the other behind. There is deliberately no single "_" — snake_case is not italic.
    private static readonly (string Open, string Close, InlineStyle Style)[] InlineMarkers =
    {
        ("**", "**", InlineStyle.Bold),
        ("__", "__", InlineStyle.Bold),
        ("~~", "~~", InlineStyle.Strikethrough),
        ("[[", "]]", InlineStyle.Link),
        ("`", "`", InlineStyle.Code),
        ("*", "*", InlineStyle.Italic)
    };

    /// <summary>How deep emphasis inside emphasis is followed before the rest is taken literally.</summary>
    private const int MaxInlineDepth = 4;

    /// <summary>
    /// Recognises the whole note. Always returns at least one block, so a brand-new note already has
    /// something to type into.
    /// </summary>
    public static List<MarkdownBlock> ParseBlocks(string? content)
    {
        var blocks = new List<MarkdownBlock>();
        MarkdownBlock? run = null;

        foreach (var line in SplitLines(content))
        {
            var block = ParseLine(line);
            if (block.Kind == MarkdownBlockKind.Paragraph)
            {
                // Consecutive prose lines join into one run: inside it Enter, wrapping and backspace
                // are the platform's own, which is the only way deleting an empty line can work at all.
                if (run == null)
                {
                    run = block;
                    blocks.Add(run);
                }
                else
                {
                    run.Text = run.Text + "\n" + block.Text;
                }
            }
            else
            {
                run = null;
                blocks.Add(block);
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new MarkdownBlock());
        }

        Renumber(blocks);
        return blocks;
    }

    /// <summary>Writes the blocks back out as the markdown that gets stored.</summary>
    public static string Compose(IEnumerable<MarkdownBlock> blocks)
        => string.Join("\n", blocks.Select(ComposeBlock));

    /// <summary>
    /// True when this line has become a marked block — the test behind "the marker vanishes the
    /// moment you type it". Also how an existing block changes kind: a bullet whose text starts
    /// <c>[]</c> becomes a tick box.
    /// </summary>
    public static bool TryPromote(string? text, out MarkdownBlock block)
    {
        block = ParseLine(text ?? string.Empty);
        return block.Kind != MarkdownBlockKind.Paragraph;
    }

    /// <summary>
    /// Finds the first line of a prose run that has turned into a marked block and cuts the run
    /// there. <paramref name="before"/> and <paramref name="after"/> are null when there is no
    /// prose on that side — an empty string means there is, and it is a blank line.
    /// </summary>
    /// <remarks>
    /// The <em>first</em> such line, because on any single keystroke it is the only one that can be
    /// new: anything earlier would have been promoted a keystroke ago. Pasted text with several
    /// markers is handled by the caller running this again on what is left.
    /// </remarks>
    public static bool TrySplitRun(string? runText, out string? before, out MarkdownBlock promoted, out string? after)
    {
        before = null;
        after = null;
        promoted = new MarkdownBlock();

        var lines = (runText ?? string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!TryPromote(lines[i], out var block)) continue;

            promoted = block;
            if (i > 0) before = string.Join("\n", lines.Take(i));
            if (i < lines.Length - 1) after = string.Join("\n", lines.Skip(i + 1));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Numbers each run of consecutive numbered items 1, 2, 3… and clears the number on everything
    /// else. Called after any structural change, so inserting an item in the middle renumbers the
    /// rest instead of leaving two items called "3".
    /// </summary>
    public static void Renumber(IList<MarkdownBlock> blocks)
    {
        var n = 0;
        foreach (var block in blocks)
        {
            if (block.Kind == MarkdownBlockKind.Numbered)
            {
                block.Number = ++n;
            }
            else
            {
                n = 0;
                block.Number = 0;
            }
        }
    }

    /// <summary>The whole note as one line of readable text — markers and emphasis gone. For the
    /// preview under a note's title in the list.</summary>
    public static string ToPlainText(string? content)
    {
        var parts = ParseBlocks(content)
            .SelectMany(b => b.Text.Split('\n'))
            .Select(line => StripInline(line).Trim())
            .Where(line => line.Length > 0);

        return string.Join(" ", parts);
    }

    /// <summary>The first line that says anything, as readable text. This is a note's title.</summary>
    public static string FirstPlainLine(string? content)
    {
        foreach (var block in ParseBlocks(content))
        {
            foreach (var line in block.Text.Split('\n'))
            {
                var plain = StripInline(line).Trim();
                if (plain.Length > 0) return plain;
            }
        }

        return string.Empty;
    }

    /// <summary>Drops <c>**</c>, <c>*</c>, <c>~~</c>, backticks and the brackets around a
    /// <c>[[link]]</c>, keeping the words. Lone <c>_</c> is left alone so snake_case survives.</summary>
    public static string StripInline(string? text)
        => string.Concat(ParseInline(text).Select(span => span.Text));

    /// <summary>
    /// Cuts one line into the stretches that are drawn differently — this is what lets the screen show
    /// <b>bold</b> instead of <c>**bold**</c>.
    /// </summary>
    /// <remarks>
    /// A line with no markup comes back as one span, so the common case allocates almost nothing. The
    /// same reading is behind <see cref="StripInline"/> and <see cref="HasInlineMarkup"/>: three
    /// separate notions of what counts as emphasis would eventually disagree, and the note's preview
    /// would then show markup that the editor had already hidden.
    /// <para>
    /// Openers are matched to the nearest closer, not by CommonMark's delimiter runs. Emphasis inside
    /// emphasis works — <c>**bold with `code`**</c> — but the two written against each other,
    /// <c>**bold and *italic***</c>, leaves a star on screen. Getting that right is a rewrite of the
    /// whole scanner for a case a diary note does not contain.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<InlineSpan> ParseInline(string? text)
    {
        var spans = new List<InlineSpan>();
        AppendInline(spans, text ?? string.Empty, InlineStyle.None, 0);
        return spans;
    }

    /// <summary>True when a line has anything worth drawing differently.</summary>
    public static bool HasInlineMarkup(string? text)
        => ParseInline(text).Any(span => span.Style != InlineStyle.None);

    private static void AppendInline(List<InlineSpan> spans, string text, InlineStyle inherited, int depth)
    {
        var literalFrom = 0;

        for (var i = 0; i < text.Length && depth < MaxInlineDepth; i++)
        {
            if (!TryMatchMarker(text, i, out var style, out var contentFrom, out var contentTo, out var next))
            {
                continue;
            }

            Emit(spans, text[literalFrom..i], inherited);

            var content = text[contentFrom..contentTo];
            if (style is InlineStyle.Code or InlineStyle.Link)
            {
                // Nothing inside a link title or a code span is markup: that is the point of both.
                Emit(spans, content, inherited | style);
            }
            else
            {
                AppendInline(spans, content, inherited | style, depth + 1);
            }

            i = next - 1;
            literalFrom = next;
        }

        Emit(spans, text[literalFrom..], inherited);
    }

    private static bool TryMatchMarker(string text, int at, out InlineStyle style, out int contentFrom, out int contentTo, out int next)
    {
        style = InlineStyle.None;
        contentFrom = contentTo = next = 0;

        foreach (var (open, close, candidate) in InlineMarkers)
        {
            if (string.CompareOrdinal(text, at, open, 0, open.Length) != 0) continue;

            var from = at + open.Length;
            var to = text.IndexOf(close, from, StringComparison.Ordinal);
            if (to <= from) continue;

            // "2 * 3 * 4" is arithmetic, not italics. Emphasis never opens or closes against a space,
            // which is the one rule that keeps stray stars from swallowing half a sentence.
            if (candidate is not (InlineStyle.Code or InlineStyle.Link)
                && (char.IsWhiteSpace(text[from]) || char.IsWhiteSpace(text[to - 1])))
            {
                continue;
            }

            style = candidate;
            contentFrom = from;
            contentTo = to;
            next = to + close.Length;
            return true;
        }

        return false;
    }

    private static void Emit(List<InlineSpan> spans, string text, InlineStyle style)
    {
        if (text.Length == 0) return;

        // Neighbours that look the same are one span: the label draws fewer pieces, and a caret
        // walking the line has nothing to catch on.
        if (spans.Count > 0 && spans[^1].Style == style)
        {
            spans[^1].Text += text;
            return;
        }

        spans.Add(new InlineSpan(text, style));
    }

    private static IEnumerable<string> SplitLines(string? content)
        => (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static MarkdownBlock ParseLine(string line)
    {
        Match match;

        if ((match = HeadingPattern.Match(line)).Success)
        {
            return new MarkdownBlock
            {
                Kind = match.Groups[2].Value.Length switch
                {
                    1 => MarkdownBlockKind.Heading1,
                    2 => MarkdownBlockKind.Heading2,
                    _ => MarkdownBlockKind.Heading3
                },
                Indent = match.Groups[1].Value,
                Text = match.Groups[3].Value
            };
        }

        if ((match = ChecklistPattern.Match(line)).Success)
        {
            return new MarkdownBlock
            {
                Kind = MarkdownBlockKind.Checklist,
                Indent = match.Groups[1].Value,
                IsChecked = match.Groups[2].Value is "x" or "X",
                Text = match.Groups[3].Value
            };
        }

        if ((match = NumberedPattern.Match(line)).Success)
        {
            return new MarkdownBlock
            {
                Kind = MarkdownBlockKind.Numbered,
                Indent = match.Groups[1].Value,
                Text = match.Groups[3].Value
            };
        }

        if ((match = BulletPattern.Match(line)).Success)
        {
            return new MarkdownBlock
            {
                Kind = MarkdownBlockKind.Bullet,
                Indent = match.Groups[1].Value,
                Text = match.Groups[2].Value
            };
        }

        if ((match = QuotePattern.Match(line)).Success)
        {
            return new MarkdownBlock
            {
                Kind = MarkdownBlockKind.Quote,
                Indent = match.Groups[1].Value,
                Text = match.Groups[2].Value
            };
        }

        return new MarkdownBlock { Text = line };
    }

    private static string ComposeBlock(MarkdownBlock block) => block.Kind switch
    {
        MarkdownBlockKind.Heading1 => block.Indent + "# " + block.Text,
        MarkdownBlockKind.Heading2 => block.Indent + "## " + block.Text,
        MarkdownBlockKind.Heading3 => block.Indent + "### " + block.Text,
        MarkdownBlockKind.Bullet => block.Indent + "- " + block.Text,
        MarkdownBlockKind.Numbered => block.Indent + (block.Number > 0 ? block.Number : 1) + ". " + block.Text,
        MarkdownBlockKind.Checklist => block.Indent + (block.IsChecked ? "- [x] " : "- [ ] ") + block.Text,
        MarkdownBlockKind.Quote => block.Indent + "> " + block.Text,
        _ => block.Text
    };
}
