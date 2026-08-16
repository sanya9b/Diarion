using System.Collections.Generic;
using Diarion.Models.Markdown;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// One line of a note deciding how it is drawn. A MAUI input field cannot format part of its own text,
/// so a line that has something to show becomes a label until it is tapped — and a line that has
/// nothing to show must never do that, because a label cannot say which character the finger landed on.
/// </summary>
public class NoteBlockViewModelTests
{
    private static NoteBlockViewModel Block(
        string text,
        MarkdownBlockKind kind = MarkdownBlockKind.Paragraph,
        bool isChecked = false)
        => new(new MarkdownBlock { Kind = kind, Text = text, IsChecked = isChecked });

    [Fact]
    public void APlainLineStaysAFieldYouCanPutTheCaretInto()
    {
        var block = Block("купити молоко");

        block.ShowsFormattedText.Should().BeFalse();
        block.ShowsRawText.Should().BeTrue();
    }

    [Fact]
    public void ALineWithEmphasisIsDrawnRatherThanShownAsItIsWritten()
    {
        var block = Block("купити **молоко**");

        block.ShowsFormattedText.Should().BeTrue();
        block.ShowsRawText.Should().BeFalse();
    }

    [Fact]
    public void AnEmptyLineIsAFieldSoThereIsSomewhereToType()
    {
        Block(string.Empty).ShowsRawText.Should().BeTrue();
    }

    [Fact]
    public void TheMarkupShowsItselfWhileTheCaretIsInTheLine()
    {
        var block = Block("купити **молоко**");

        block.HoldEditCommand.Execute(null);

        block.ShowsRawText.Should().BeTrue();
        block.ShowsFormattedText.Should().BeFalse();
    }

    [Fact]
    public void TheMarkupGoesBackToBeingDrawnWhenTheCaretLeaves()
    {
        var block = Block("купити **молоко**");
        block.HoldEditCommand.Execute(null);

        block.EndEditCommand.Execute(null);

        block.ShowsFormattedText.Should().BeTrue();
    }

    [Fact]
    public void TappingADrawnLinePutsTheCaretAtItsEnd()
    {
        // A label cannot say which character was tapped, so the end is the only honest answer.
        var block = Block("купити **молоко**");

        block.BeginEditCommand.Execute(null);

        block.ShowsRawText.Should().BeTrue();
        block.IsFocusRequested.Should().BeTrue();
        block.Caret.Should().Be("купити **молоко**".Length);
    }

    [Fact]
    public void TypingEmphasisDoesNotYankTheFieldAwayMidWord()
    {
        // The line has become worth drawing, but the caret is still in it: swapping now would take the
        // field out from under the user between the second and the third star.
        var block = Block("купити ");
        block.HoldEditCommand.Execute(null);

        block.Text = "купити **молоко**";

        block.ShowsRawText.Should().BeTrue();
    }

    [Fact]
    public void ATickedItemIsCrossedOutEvenWithNothingElseToDraw()
    {
        var block = Block("молоко", MarkdownBlockKind.Checklist, isChecked: true);

        block.IsStruck.Should().BeTrue();
        block.ShowsFormattedText.Should().BeTrue();
    }

    [Fact]
    public void AnItemThatIsNotTickedIsNotCrossedOut()
    {
        var block = Block("молоко", MarkdownBlockKind.Checklist);

        block.IsStruck.Should().BeFalse();
        block.ShowsRawText.Should().BeTrue();
    }

    [Fact]
    public void OnlyATickBoxCanBeCrossedOut()
    {
        // IsChecked is stored on every block; a bullet that happens to carry one is still a bullet.
        Block("молоко", MarkdownBlockKind.Bullet, isChecked: true).IsStruck.Should().BeFalse();
    }

    [Fact]
    public void TickingAnItemRedrawsIt()
    {
        var block = Block("молоко", MarkdownBlockKind.Checklist);
        var announced = new List<string?>();
        block.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        block.ToggleCheckCommand.Execute(null);

        block.IsChecked.Should().BeTrue();
        announced.Should().Contain(nameof(NoteBlockViewModel.IsStruck));
        announced.Should().Contain(nameof(NoteBlockViewModel.ShowsFormattedText));
    }

    [Fact]
    public void EditingALineRedrawsWhatItSays()
    {
        var block = Block("молоко");
        var announced = new List<string?>();
        block.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        block.Text = "**молоко**";

        announced.Should().Contain(nameof(NoteBlockViewModel.Spans));
    }

    [Fact]
    public void TheLineKnowsWhichStretchesAreDrawnDifferently()
    {
        var block = Block("купити **молоко**");

        block.Spans.Should().HaveCount(2);
        block.Spans[1].Text.Should().Be("молоко");
        block.Spans[1].Has(InlineStyle.Bold).Should().BeTrue();
    }
}
