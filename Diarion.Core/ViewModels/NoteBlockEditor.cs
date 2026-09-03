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
/// <para>
/// Every cut and join here is made on <see cref="StyledText"/>, not on strings, so a bold word keeps
/// its weight when the line it is in becomes a bullet or is split by Enter.
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

        last.RequestFocus(last.Body.Length);
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
    /// Bold, italic or strikethrough, the way every rich text editor does it: with text selected, the
    /// selection takes the style — or loses it, if it already had it throughout; with nothing selected,
    /// what is typed next takes it. Neither path ever shows the user a marker.
    /// </summary>
    /// <remarks>
    /// Flipping the typing style is not an edit: nothing stored has changed until a character is
    /// typed, so no save is raised for it.
    /// </remarks>
    public void ToggleStyle(InlineStyle style)
    {
        var block = Target();
        if (block == null || style == InlineStyle.None) return;

        var body = block.Body;
        var start = Math.Clamp(block.SelectionStart, 0, body.Length);
        var length = Math.Clamp(block.SelectionLength, 0, body.Length - start);

        if (length == 0)
        {
            block.TypingStyle ^= style;
            return;
        }

        block.Body = body.Toggle(start, length, style);
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
            var shape = new MarkdownBlock { Kind = kind, Indent = block.Indent };
            focus = ChangeKind(block, shape, block.Body, out caret);
        }

        Renumber();
        (focus, caret) = Compact(focus, caret);
        UpdatePlaceholder();

        Active = focus;
        focus?.RequestFocus(caret);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Prose is held in runs, so "make this a bullet" means one line out of possibly many. Which line
    // is the one the caret is in — the only reason the blocks track the caret at all.
    private NoteBlockViewModel PromoteLine(NoteBlockViewModel run, MarkdownBlockKind kind, out int caret)
    {
        var body = run.Body;
        var text = body.Text;
        var at = Math.Clamp(run.SelectionStart, 0, text.Length);

        var from = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        var to = text.IndexOf('\n', at);
        if (to < 0) to = text.Length;

        caret = to - from;

        var index = Blocks.IndexOf(run);
        if (index < 0) return run;

        var promoted = Wrap(new MarkdownBlock { Kind = kind, Indent = run.Indent }, body.Slice(from, to));

        var replacements = new List<NoteBlockViewModel>();
        if (from > 0) replacements.Add(Wrap(new MarkdownBlock(), body.Slice(0, from - 1)));
        replacements.Add(promoted);
        if (to < text.Length) replacements.Add(Wrap(new MarkdownBlock(), body.Slice(to + 1, body.Length)));

        Blocks.RemoveAt(index);
        for (var i = 0; i < replacements.Count; i++)
        {
            Blocks.Insert(index + i, replacements[i]);
        }

        return promoted;
    }

    private NoteBlockViewModel Wrap(MarkdownBlock block) => new(block, OnBlockEdited, OnBlockFocused);

    // A block whose text is already styled: the shape says what kind it is, the body what it says.
    private NoteBlockViewModel Wrap(MarkdownBlock shape, StyledText body)
    {
        var block = Wrap(shape);
        block.SetBodySilently(body);
        return block;
    }

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
        else if (block.Text.Contains('\n'))
        {
            focus = SplitLine(block, out caret);
        }
        else if (MarkdownParser.TryPromote(block.Text, out var promoted))
        {
            // The marker is a prefix of the line and the recognised text its suffix, so the styled
            // remainder is the same count of characters off the end.
            var body = block.Body;
            focus = ChangeKind(block, promoted, body.Slice(body.Length - promoted.Text.Length, body.Length), out caret);
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
            && Blocks[0].Body.IsEmpty;

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

    // The first marked line, because on any single keystroke it is the only one that can be new:
    // anything earlier would have been promoted a keystroke ago.
    private NoteBlockViewModel? SplitRunOnce(NoteBlockViewModel run, out NoteBlockViewModel? tail, out int promotedLength)
    {
        tail = null;
        promotedLength = 0;

        var body = run.Body;
        var lines = body.Text.Split('\n');
        var offset = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineEnd = offset + line.Length;

            if (!MarkdownParser.TryPromote(line, out var promoted))
            {
                offset = lineEnd + 1;
                continue;
            }

            var index = Blocks.IndexOf(run);
            if (index < 0) return null;

            var replacements = new List<NoteBlockViewModel>();
            if (i > 0) replacements.Add(Wrap(new MarkdownBlock(), body.Slice(0, offset - 1)));

            var promotedBlock = Wrap(promoted, body.Slice(lineEnd - promoted.Text.Length, lineEnd));
            replacements.Add(promotedBlock);

            if (i < lines.Length - 1)
            {
                tail = Wrap(new MarkdownBlock(), body.Slice(lineEnd + 1, body.Length));
                replacements.Add(tail);
            }

            Blocks.RemoveAt(index);
            for (var j = 0; j < replacements.Count; j++)
            {
                Blocks.Insert(index + j, replacements[j]);
            }

            promotedLength = promoted.Text.Length;
            return promotedBlock;
        }

        return null;
    }

    // A newline has arrived in a block that holds exactly one line: the user pressed Enter.
    private NoteBlockViewModel? SplitLine(NoteBlockViewModel block, out int caret)
    {
        caret = 0;

        var body = block.Body;
        var at = body.Text.IndexOf('\n');
        var before = body.Slice(0, at);
        var after = body.Slice(at + 1, body.Length);

        var index = Blocks.IndexOf(block);
        if (index < 0) return null;

        // Enter on an item that has nothing in it is how you leave a list: the marker comes off
        // instead of another empty item appearing below it.
        if (before.IsEmpty && after.IsEmpty && IsListItem(block.Kind))
        {
            var plain = Wrap(new MarkdownBlock());
            Blocks.RemoveAt(index);
            Blocks.Insert(index, plain);
            return plain;
        }

        block.SetBodySilently(before);

        // Pasted text brings its own line breaks; hand the whole tail to the prose path, which knows
        // how to pull markers back out of it. Otherwise the new line carries on the list, and Enter
        // after a heading drops you into ordinary text — the same as every editor that has headings.
        var carriesBreaks = after.Text.Contains('\n');
        var nextKind = carriesBreaks || IsHeading(block.Kind) ? MarkdownBlockKind.Paragraph : block.Kind;

        var next = Wrap(new MarkdownBlock { Kind = nextKind, Indent = block.Indent }, after);
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

    // The block has to become another kind — "[]" typed in front of a bullet, or a button pressed.
    // It cannot change kind in place, so it is swapped for one of the new kind carrying the same text.
    private NoteBlockViewModel ChangeKind(NoteBlockViewModel block, MarkdownBlock shape, StyledText body, out int caret)
    {
        var index = Blocks.IndexOf(block);
        if (shape.Indent.Length == 0) shape.Indent = block.Indent;

        var replacement = Wrap(shape, body);
        Blocks.RemoveAt(index);
        Blocks.Insert(index, replacement);

        caret = body.Length;
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

            var join = first.Body.Length + 1;
            first.SetBodySilently(first.Body.Append("\n").Concat(second.Body));
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
