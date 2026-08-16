using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Diarion.Models.Markdown;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// The body of a note as a list of editable blocks, and the rules that reshape that list while it is
/// being typed into.
/// </summary>
/// <remarks>
/// <para>
/// Split out of <see cref="NoteDetailViewModel"/> because it is the only interesting logic on that
/// screen and it needs no database, no navigation and no dialogs to be tested.
/// </para>
/// <para>
/// Prose is held as one multi-line block rather than one block per line. That is not an optimisation:
/// it is what makes Enter, wrapping and — above all — backspacing an empty line away behave the way
/// they do in any other text field, because inside a run they are the platform's own behaviour and
/// not something reimplemented here. Only marked lines get a block of their own, and the only
/// keystroke this class has to recognise is the newline that arrives in a block that cannot hold one.
/// </para>
/// </remarks>
public class NoteBlockEditor
{
    private bool _loading;

    public NoteBlockEditor()
    {
        Load(null);
    }

    public ObservableCollection<NoteBlockViewModel> Blocks { get; } = new();

    /// <summary>Raised once the block list has settled after an edit — the cue to recompose and save.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The line the formatting bar acts on: the one the caret is in, or the one it was in last.
    /// </summary>
    /// <remarks>
    /// It survives the field losing focus on purpose. Pressing a button in the bar is exactly the
    /// moment focus leaves the text — on Windows it does, on the phones it does not — and a target
    /// that were cleared on blur would be null in the only case that matters.
    /// </remarks>
    public NoteBlockViewModel? Active { get; private set; }

    /// <summary>Replaces the whole body. Always leaves at least one block to type into.</summary>
    public void Load(string? content)
    {
        _loading = true;
        try
        {
            Active = null;
            Blocks.Clear();
            foreach (var block in MarkdownParser.ParseBlocks(content))
            {
                Blocks.Add(Wrap(block));
            }
        }
        finally
        {
            _loading = false;
        }

        UpdatePlaceholder();
    }

    /// <summary>The markdown to store.</summary>
    public string Compose() => MarkdownParser.Compose(Blocks.Select(b => b.ToBlock()));

    /// <summary>
    /// Puts the caret at the very end of the note, adding a line to land on when the last block is
    /// something you cannot simply carry on typing into. For the tap on the empty space below a note.
    /// </summary>
    public void FocusEnd()
    {
        if (Blocks.Count == 0)
        {
            Blocks.Add(Wrap(new MarkdownBlock()));
        }

        var last = Blocks[^1];
        if (last.Kind != MarkdownBlockKind.Paragraph)
        {
            last = Wrap(new MarkdownBlock());
            Blocks.Add(last);
            UpdatePlaceholder();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        last.RequestFocus((last.Text ?? string.Empty).Length);
    }

    /// <summary>
    /// Turns the current line into a list item, a tick box or a quote — and back into prose when it is
    /// already that kind, which is what makes one button both apply and remove.
    /// </summary>
    /// <remarks>
    /// Typing "- " still works and always will. This is the same operation reached without knowing the
    /// symbol, which is the whole point of the bar.
    /// </remarks>
    public void ToggleKind(MarkdownBlockKind kind)
    {
        var block = Target();
        if (block == null) return;

        SetKind(block, block.Kind == kind ? MarkdownBlockKind.Paragraph : kind);
    }

    /// <summary>
    /// One button for three sizes: H1 → H2 → H3 → plain text. A fourth button per level would take a
    /// third of the bar to say something the user can see on screen after one press.
    /// </summary>
    public void CycleHeading()
    {
        var block = Target();
        if (block == null) return;

        SetKind(block, block.Kind switch
        {
            MarkdownBlockKind.Heading1 => MarkdownBlockKind.Heading2,
            MarkdownBlockKind.Heading2 => MarkdownBlockKind.Heading3,
            MarkdownBlockKind.Heading3 => MarkdownBlockKind.Paragraph,
            _ => MarkdownBlockKind.Heading1
        });
    }

    /// <summary>
    /// Wraps the selection in <paramref name="marker"/> — <c>**</c>, <c>*</c> or <c>~~</c> — and
    /// unwraps it when it is already wrapped.
    /// </summary>
    /// <remarks>
    /// With nothing selected it takes the word the caret is standing in. That is not a convenience:
    /// on a phone selecting a word first is two gestures, and a button that inserted an empty pair of
    /// stars would be a button that asks you to type between them.
    /// </remarks>
    public void ToggleInline(string marker)
    {
        var block = Target();
        if (block == null || string.IsNullOrEmpty(marker)) return;

        var text = block.Text ?? string.Empty;
        var (start, length) = Resolve(block, text);

        string next;
        int caret;

        if (WrapsFromOutside(text, start, length, marker))
        {
            next = text
                .Remove(start + length, marker.Length)
                .Remove(start - marker.Length, marker.Length);
            caret = start + length - marker.Length;
        }
        else if (WrapsFromInside(text, start, length, marker))
        {
            next = text
                .Remove(start + length - marker.Length, marker.Length)
                .Remove(start, marker.Length);
            caret = start + length - (2 * marker.Length);
        }
        else
        {
            next = text.Insert(start + length, marker).Insert(start, marker);
            caret = length == 0 ? start + marker.Length : start + length + (2 * marker.Length);
        }

        // The line has to stay a field: markup makes it eligible to be drawn as a formatted label, and
        // being swapped for a label under the caret is not what pressing "bold" asks for.
        block.IsEditing = true;
        block.Text = next;
        block.RequestFocus(caret);
    }

    // Which line the bar acts on. Falling back to the last block covers the note that has been opened
    // and not yet typed into: the bar is on screen, so it has to do something.
    private NoteBlockViewModel? Target()
    {
        if (Active != null && Blocks.Contains(Active)) return Active;

        Active = Blocks.Count > 0 ? Blocks[^1] : null;
        return Active;
    }

    private void SetKind(NoteBlockViewModel block, MarkdownBlockKind kind)
    {
        if (block.Kind == kind) return;

        int caret;
        NoteBlockViewModel? focus;

        if (block.Kind == MarkdownBlockKind.Paragraph)
        {
            focus = PromoteLine(block, kind, out caret);
        }
        else
        {
            var replacement = new MarkdownBlock
            {
                Kind = kind,
                Text = block.Text ?? string.Empty,
                Indent = block.Indent
            };

            focus = ChangeKind(block, replacement, out caret);
        }

        Renumber();
        (focus, caret) = Compact(focus, caret);
        UpdatePlaceholder();

        Active = focus;
        if (focus != null)
        {
            focus.IsEditing = true;
            focus.RequestFocus(caret);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Prose is held in runs, so "make this a bullet" means one line out of possibly many. Which line
    // is the one the caret is in — the only reason the blocks track the caret at all.
    private NoteBlockViewModel PromoteLine(NoteBlockViewModel run, MarkdownBlockKind kind, out int caret)
    {
        var text = run.Text ?? string.Empty;
        var at = Math.Clamp(run.SelectionStart, 0, text.Length);

        var from = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        var to = text.IndexOf('\n', at);
        if (to < 0) to = text.Length;

        var line = text[from..to];
        caret = line.Length;

        var index = Blocks.IndexOf(run);
        if (index < 0) return run;

        var promoted = Wrap(new MarkdownBlock { Kind = kind, Text = line, Indent = run.Indent });

        var replacements = new List<NoteBlockViewModel>();
        if (from > 0) replacements.Add(Wrap(new MarkdownBlock { Text = text[..(from - 1)] }));
        replacements.Add(promoted);
        if (to < text.Length) replacements.Add(Wrap(new MarkdownBlock { Text = text[(to + 1)..] }));

        Blocks.RemoveAt(index);
        for (var i = 0; i < replacements.Count; i++)
        {
            Blocks.Insert(index + i, replacements[i]);
        }

        return promoted;
    }

    private static (int Start, int Length) Resolve(NoteBlockViewModel block, string text)
    {
        var start = Math.Clamp(block.SelectionStart, 0, text.Length);
        var length = Math.Clamp(block.SelectionLength, 0, text.Length - start);
        if (length > 0) return (start, length);

        var from = start;
        while (from > 0 && !char.IsWhiteSpace(text[from - 1])) from--;

        var to = start;
        while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;

        return (from, to - from);
    }

    // The markers sit outside the selection: "**|word|**".
    private static bool WrapsFromOutside(string text, int start, int length, string marker)
    {
        if (length == 0 || start < marker.Length || start + length + marker.Length > text.Length)
        {
            return false;
        }

        return Matches(text, start - marker.Length, marker)
            && Matches(text, start + length, marker)
            && !IsHalfOfBold(text, start - marker.Length - 1, marker);
    }

    // The selection took the markers with it: "|**word**|". A double tap picks the word, a drag
    // usually picks more than the word, and both have to toggle rather than add a second pair.
    private static bool WrapsFromInside(string text, int start, int length, string marker)
    {
        if (length < 2 * marker.Length) return false;

        return Matches(text, start, marker)
            && Matches(text, start + length - marker.Length, marker)
            && !IsHalfOfBold(text, start + marker.Length, marker);
    }

    // "*" next to another "*" is half of "**": italic must not strip a bold pair down the middle.
    private static bool IsHalfOfBold(string text, int at, string marker)
        => marker == "*" && at >= 0 && at < text.Length && text[at] == '*';

    private static bool Matches(string text, int at, string marker)
        => at >= 0
            && at + marker.Length <= text.Length
            && string.CompareOrdinal(text, at, marker, 0, marker.Length) == 0;

    private NoteBlockViewModel Wrap(MarkdownBlock block) => new(block, OnBlockEdited, OnBlockFocused);

    private void OnBlockFocused(NoteBlockViewModel block) => Active = block;

    private void OnBlockEdited(NoteBlockViewModel block)
    {
        if (_loading) return;

        Restructure(block);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Restructure(NoteBlockViewModel block)
    {
        NoteBlockViewModel? focus = null;
        var caret = 0;

        if (block.Kind == MarkdownBlockKind.Paragraph)
        {
            focus = SplitRun(block, out caret);
        }
        else if ((block.Text ?? string.Empty).Contains('\n'))
        {
            focus = SplitLine(block, out caret);
        }
        else if (MarkdownParser.TryPromote(block.Text, out var promoted))
        {
            focus = ChangeKind(block, promoted, out caret);
        }

        Renumber();
        (focus, caret) = Compact(focus, caret);
        UpdatePlaceholder();
        focus?.RequestFocus(caret);
    }

    // "Write something" belongs to an empty note, not to every empty line: the blank line between a
    // heading and a list is an empty block too, and it must stay silent.
    private void UpdatePlaceholder()
    {
        var isEmptyNote = Blocks.Count == 1
            && Blocks[0].Kind == MarkdownBlockKind.Paragraph
            && string.IsNullOrEmpty(Blocks[0].Text);

        foreach (var block in Blocks)
        {
            block.ShowsPlaceholder = isEmptyNote;
        }
    }

    // A run of prose in which one line has just become a marked block: cut the run there. Repeated
    // until the tail is prose again, because a paste can carry a whole list at once.
    private NoteBlockViewModel? SplitRun(NoteBlockViewModel run, out int caret)
    {
        caret = 0;

        var first = SplitRunOnce(run, out var tail, out var promotedLength);
        if (first == null) return null;

        caret = promotedLength;
        while (tail != null)
        {
            SplitRunOnce(tail, out tail, out _);
        }

        return first;
    }

    private NoteBlockViewModel? SplitRunOnce(NoteBlockViewModel run, out NoteBlockViewModel? tail, out int promotedLength)
    {
        tail = null;
        promotedLength = 0;

        if (!MarkdownParser.TrySplitRun(run.Text, out var before, out var promoted, out var after))
        {
            return null;
        }

        var index = Blocks.IndexOf(run);
        if (index < 0) return null;

        var replacements = new List<NoteBlockViewModel>();
        if (before != null) replacements.Add(Wrap(new MarkdownBlock { Text = before }));

        var promotedBlock = Wrap(promoted);
        replacements.Add(promotedBlock);

        if (after != null)
        {
            tail = Wrap(new MarkdownBlock { Text = after });
            replacements.Add(tail);
        }

        Blocks.RemoveAt(index);
        for (var i = 0; i < replacements.Count; i++)
        {
            Blocks.Insert(index + i, replacements[i]);
        }

        promotedLength = promoted.Text.Length;
        return promotedBlock;
    }

    // A newline has arrived in a block that holds exactly one line: the user pressed Enter.
    private NoteBlockViewModel? SplitLine(NoteBlockViewModel block, out int caret)
    {
        caret = 0;

        var text = block.Text ?? string.Empty;
        var at = text.IndexOf('\n');
        var before = text[..at];
        var after = text[(at + 1)..];

        var index = Blocks.IndexOf(block);
        if (index < 0) return null;

        // Enter on an item that has nothing in it is how you leave a list: the marker comes off
        // instead of another empty item appearing below it.
        if (before.Length == 0 && after.Length == 0 && IsListItem(block.Kind))
        {
            var plain = Wrap(new MarkdownBlock());
            Blocks.RemoveAt(index);
            Blocks.Insert(index, plain);
            return plain;
        }

        block.SetTextSilently(before);

        // Pasted text brings its own line breaks; hand the whole tail to the prose path, which knows
        // how to pull markers back out of it. Otherwise the new line carries on the list, and Enter
        // after a heading drops you into ordinary text — the same as every editor that has headings.
        var carriesBreaks = after.Contains('\n');
        var nextKind = carriesBreaks || IsHeading(block.Kind) ? MarkdownBlockKind.Paragraph : block.Kind;

        var next = Wrap(new MarkdownBlock { Kind = nextKind, Text = after, Indent = block.Indent });
        Blocks.Insert(index + 1, next);

        if (carriesBreaks)
        {
            var promoted = SplitRun(next, out var promotedCaret);
            if (promoted != null)
            {
                caret = promotedCaret;
                return promoted;
            }
        }

        return next;
    }

    // The block's text now starts with a marker — "- " typed in front of a heading, "[]" in front of
    // a bullet. The block cannot change kind in place, so it is swapped for one of the new kind.
    private NoteBlockViewModel ChangeKind(NoteBlockViewModel block, MarkdownBlock promoted, out int caret)
    {
        var index = Blocks.IndexOf(block);
        if (promoted.Indent.Length == 0) promoted.Indent = block.Indent;

        var replacement = Wrap(promoted);
        Blocks.RemoveAt(index);
        Blocks.Insert(index, replacement);

        caret = promoted.Text.Length;
        return replacement;
    }

    // Two runs of prose next to each other are one run: joining them is what puts the blank line
    // between them back inside a text field, where backspace can reach it.
    private (NoteBlockViewModel? Focus, int Caret) Compact(NoteBlockViewModel? focus, int caret)
    {
        for (var i = Blocks.Count - 2; i >= 0; i--)
        {
            var first = Blocks[i];
            var second = Blocks[i + 1];
            if (first.Kind != MarkdownBlockKind.Paragraph || second.Kind != MarkdownBlockKind.Paragraph)
            {
                continue;
            }

            var join = (first.Text ?? string.Empty).Length + 1;
            first.SetTextSilently((first.Text ?? string.Empty) + "\n" + (second.Text ?? string.Empty));
            Blocks.RemoveAt(i + 1);

            if (ReferenceEquals(focus, second))
            {
                focus = first;
                caret += join;
            }
        }

        return (focus, caret);
    }

    private void Renumber()
    {
        var n = 0;
        foreach (var block in Blocks)
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

    private static bool IsListItem(MarkdownBlockKind kind)
        => kind is MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered
            or MarkdownBlockKind.Checklist or MarkdownBlockKind.Quote;

    private static bool IsHeading(MarkdownBlockKind kind)
        => kind is MarkdownBlockKind.Heading1 or MarkdownBlockKind.Heading2 or MarkdownBlockKind.Heading3;
}
