using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Services;

namespace Diarion.Models.Markdown;

/// <summary>
/// One line of a note the way the screen holds it: the characters, and for each stretch of them the
/// look it has. No markup anywhere in here — <c>**</c> is something this is composed into on the way
/// to the database and parsed out of on the way back, never something the user sees.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and always normalised: no empty runs, and no two neighbours with the same style. That
/// makes equality structural and cheap, which the editor controls lean on to tell "the text came back
/// from the view model exactly as I sent it" apart from "redraw".
/// </para>
/// <para>
/// Offsets throughout are UTF-16 indices into <see cref="Text"/>, the same units every platform's
/// selection reports in.
/// </para>
/// </remarks>
public sealed class StyledText : IEquatable<StyledText>
{
    public static readonly StyledText Empty = new(Array.Empty<InlineSpan>(), string.Empty);

    private readonly InlineSpan[] _runs;

    private StyledText(InlineSpan[] runs, string text)
    {
        _runs = runs;
        Text = text;
    }

    /// <summary>The characters with every style forgotten.</summary>
    public string Text { get; }

    public int Length => Text.Length;

    /// <summary>The stretches that are drawn differently, in order. Never mutate the spans.</summary>
    public IReadOnlyList<InlineSpan> Runs => _runs;

    public bool IsEmpty => Text.Length == 0;

    public static StyledText Plain(string? text)
        => string.IsNullOrEmpty(text) ? Empty : new([new InlineSpan(text)], text);

    /// <summary>Builds from spans, merging neighbours that look the same and dropping empty ones.</summary>
    public static StyledText FromRuns(IEnumerable<InlineSpan> runs)
    {
        var list = new List<InlineSpan>();
        foreach (var run in runs)
        {
            Append(list, run.Text, run.Style);
        }

        return list.Count == 0 ? Empty : new(list.ToArray(), string.Concat(list.Select(r => r.Text)));
    }

    /// <summary>What a stored line reads as: the markdown parsed, the markers gone.</summary>
    public static StyledText Parse(string? markdown) => FromRuns(MarkdownParser.ParseInline(markdown));

    /// <summary>The characters from <paramref name="from"/> up to but not including <paramref name="to"/>.</summary>
    public StyledText Slice(int from, int to)
    {
        from = Math.Clamp(from, 0, Length);
        to = Math.Clamp(to, from, Length);
        if (from == 0 && to == Length) return this;
        if (from == to) return Empty;

        var list = new List<InlineSpan>();
        var at = 0;
        foreach (var run in _runs)
        {
            var runEnd = at + run.Text.Length;
            var start = Math.Max(from, at);
            var end = Math.Min(to, runEnd);
            if (end > start) Append(list, run.Text.Substring(start - at, end - start), run.Style);
            at = runEnd;
            if (at >= to) break;
        }

        return FromRuns(list);
    }

    public StyledText Concat(StyledText other)
    {
        if (other.IsEmpty) return this;
        if (IsEmpty) return other;
        return FromRuns(_runs.Concat(other._runs));
    }

    public StyledText Append(string text, InlineStyle style = InlineStyle.None)
        => string.IsNullOrEmpty(text) ? this : Concat(new StyledText([new InlineSpan(text, style)], text));

    /// <summary>Puts <paramref name="text"/> in at <paramref name="at"/>, drawn with <paramref name="style"/>.</summary>
    public StyledText Insert(int at, string text, InlineStyle style = InlineStyle.None)
        => Slice(0, at).Append(text, style).Concat(Slice(at, Length));

    public StyledText Remove(int from, int length)
        => Slice(0, from).Concat(Slice(from + length, Length));

    /// <summary>The style of the character at <paramref name="index"/>; <see cref="InlineStyle.None"/> past the end.</summary>
    public InlineStyle StyleAt(int index)
    {
        if (index < 0 || index >= Length) return InlineStyle.None;

        var at = 0;
        foreach (var run in _runs)
        {
            at += run.Text.Length;
            if (index < at) return run.Style;
        }

        return InlineStyle.None;
    }

    /// <summary>
    /// What the next character typed at <paramref name="caret"/> would inherit: the look of the one
    /// before it, which is how every rich text field behaves.
    /// </summary>
    public InlineStyle StyleBefore(int caret) => caret <= 0 ? InlineStyle.None : StyleAt(Math.Min(caret, Length) - 1);

    /// <summary>True when every character in the range carries <paramref name="style"/>.</summary>
    public bool HasStyle(int from, int length, InlineStyle style)
    {
        from = Math.Clamp(from, 0, Length);
        var to = Math.Clamp(from + length, from, Length);
        if (to == from) return false;

        var at = 0;
        foreach (var run in _runs)
        {
            var runEnd = at + run.Text.Length;
            if (runEnd > from && at < to && !run.Has(style)) return false;
            at = runEnd;
            if (at >= to) break;
        }

        return true;
    }

    /// <summary>Adds or removes <paramref name="style"/> across the range, leaving the other styles alone.</summary>
    public StyledText WithStyle(int from, int length, InlineStyle style, bool on)
    {
        from = Math.Clamp(from, 0, Length);
        var to = Math.Clamp(from + length, from, Length);
        if (to == from) return this;

        var middle = Slice(from, to)._runs
            .Select(run => new InlineSpan(run.Text, on ? run.Style | style : run.Style & ~style));

        return FromRuns(Slice(0, from)._runs.Concat(middle).Concat(Slice(to, Length)._runs));
    }

    /// <summary>
    /// One button, both directions: a range that is already entirely in <paramref name="style"/> loses
    /// it, anything else gains it — so a half-bold selection becomes all bold rather than half plain.
    /// </summary>
    public StyledText Toggle(int from, int length, InlineStyle style)
        => WithStyle(from, length, style, !HasStyle(from, length, style));

    /// <summary>
    /// True when some run still reads as markup — the user typed <c>**молоко**</c> by hand, or pasted
    /// it. Runs already marked as code or a link are literal by definition and are never re-read.
    /// </summary>
    public bool ContainsMarkup
        => _runs.Any(run => !run.Has(InlineStyle.Code) && !run.Has(InlineStyle.Link)
                            && MarkdownParser.HasInlineMarkup(run.Text));

    /// <summary>
    /// Reads the markup the user typed and turns it into style, run by run, so that <c>**молоко**</c>
    /// typed into a line that was already italic comes out bold and italic. Returns the same instance
    /// when there is nothing to expand.
    /// </summary>
    public StyledText ExpandMarkup()
    {
        if (!ContainsMarkup) return this;

        var list = new List<InlineSpan>();
        foreach (var run in _runs)
        {
            if (run.Has(InlineStyle.Code) || run.Has(InlineStyle.Link))
            {
                Append(list, run.Text, run.Style);
                continue;
            }

            foreach (var span in MarkdownParser.ParseInline(run.Text))
            {
                Append(list, span.Text, span.Style | run.Style);
            }
        }

        return FromRuns(list);
    }

    /// <summary>Cuts at every newline. A line break never carries a style of its own.</summary>
    public IReadOnlyList<StyledText> Lines()
    {
        var lines = new List<StyledText>();
        var from = 0;
        while (true)
        {
            var at = Text.IndexOf('\n', from);
            if (at < 0)
            {
                lines.Add(Slice(from, Length));
                return lines;
            }

            lines.Add(Slice(from, at));
            from = at + 1;
        }
    }

    public bool Equals(StyledText? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_runs.Length != other._runs.Length || Text != other.Text) return false;

        for (var i = 0; i < _runs.Length; i++)
        {
            if (_runs[i].Style != other._runs[i].Style || _runs[i].Text != other._runs[i].Text) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as StyledText);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Text);
        foreach (var run in _runs) hash.Add(run.Style);
        return hash.ToHashCode();
    }

    public override string ToString() => MarkdownParser.ComposeInline(this);

    private static void Append(List<InlineSpan> list, string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (list.Count > 0 && list[^1].Style == style)
        {
            list[^1] = new InlineSpan(list[^1].Text + text, style);
            return;
        }

        list.Add(new InlineSpan(text, style));
    }
}
