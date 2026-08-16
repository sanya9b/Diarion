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

    /// <summary>Replaces the whole body. Always leaves at least one block to type into.</summary>
    public void Load(string? content)
    {
        _loading = true;
        try
        {
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

    private NoteBlockViewModel Wrap(MarkdownBlock block) => new(block, OnBlockEdited);

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
