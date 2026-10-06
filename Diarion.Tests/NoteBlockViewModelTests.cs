using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Markdown;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// One line of a note as the field holds it: styled characters and never a marker. The markdown is
/// something the line is parsed from when the note opens and composed into when it is saved.
/// </summary>
public class NoteBlockViewModelTests
{
    private static NoteBlockViewModel Block(
        string text,
        MarkdownBlockKind kind = MarkdownBlockKind.Paragraph,
        bool isChecked = false)
        => new(new MarkdownBlock { Kind = kind, Text = text, IsChecked = isChecked });

    [Fact]
    public void TheStoredMarkupArrivesAsStyleAndTheStarsDoNot()
    {
        var block = Block("купити **молоко**");

        block.Text.Should().Be("купити молоко");
        block.Body.Runs.Select(r => (r.Text, r.Style)).Should().Equal(
            ("купити ", InlineStyle.None),
            ("молоко", InlineStyle.Bold));
    }

    [Fact]
    public void TheStyleGoesBackOutAsTheSameMarkup()
    {
        Block("купити **молоко** і ~~каву~~").ToBlock().Text.Should().Be("купити **молоко** і ~~каву~~");
    }

    [Fact]
    public void MarkupTypedByHandBecomesStyleTheMomentItCloses()
    {
        // The same rule as "- " becoming a bullet: anyone who knows the symbols may still type them,
        // and they vanish into the look they describe rather than staying on screen.
        var block = Block("купити ");
        block.SelectionStart = "купити **молоко**".Length;

        block.Text = "купити **молоко**";

        block.Text.Should().Be("купити молоко");
        block.Body.StyleAt(7).Should().Be(InlineStyle.Bold);
        block.IsFocusRequested.Should().BeTrue("the field has to be told where the caret now is");
        block.Caret.Should().Be("купити молоко".Length, "four stars went away in front of it");
    }

    [Fact]
    public void MarkupTypedIntoAStyledLineAddsToTheStyleRatherThanReplacingIt()
    {
        var block = Block("*курсив*");

        block.Body = block.Body.Append(" і **жирний**", InlineStyle.Italic);

        block.Body.Runs.Last().Style.Should().Be(InlineStyle.Italic | InlineStyle.Bold);
        block.Body.Runs.Last().Text.Should().Be("жирний");
    }

    [Fact]
    public void TheLineIsSavedOnceForATypedMarkerNotTwice()
    {
        var edits = 0;
        var block = new NoteBlockViewModel(new MarkdownBlock(), onEdited: _ => edits++);

        block.Text = "**молоко**";

        edits.Should().Be(1);
    }

    [Fact]
    public void ATickedItemIsCrossedOut()
    {
        Block("молоко", MarkdownBlockKind.Checklist, isChecked: true).IsStruck.Should().BeTrue();
    }

    [Fact]
    public void AnItemThatIsNotTickedIsNotCrossedOut()
    {
        Block("молоко", MarkdownBlockKind.Checklist).IsStruck.Should().BeFalse();
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
    }

    [Fact]
    public void EditingALineAnnouncesBothTheStyledAndThePlainReading()
    {
        var block = Block("молоко");
        var announced = new List<string?>();
        block.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        block.Text = "молоко 2л";

        announced.Should().Contain(nameof(NoteBlockViewModel.Body));
        announced.Should().Contain(nameof(NoteBlockViewModel.Text));
    }

    [Fact]
    public void SettingTheSameTextAgainIsNotAnEdit()
    {
        // Two-way bindings echo. A field that reports back exactly what it was given must not cost a
        // save, and must not wipe the styles the plain text cannot carry.
        var edits = 0;
        var block = new NoteBlockViewModel(new MarkdownBlock { Text = "**молоко**" }, onEdited: _ => edits++);

        block.Text = "молоко";
        block.Body = StyledText.Parse("**молоко**");

        edits.Should().Be(0);
        block.Body.StyleAt(0).Should().Be(InlineStyle.Bold);
    }

    [Fact]
    public void TakingFocusTellsTheEditorWhichLineIsLive()
    {
        NoteBlockViewModel? focused = null;
        var block = new NoteBlockViewModel(new MarkdownBlock(), onFocused: b => focused = b);

        block.HoldEditCommand.Execute(null);

        focused.Should().BeSameAs(block);
        block.IsEditing.Should().BeTrue();

        block.EndEditCommand.Execute(null);
        block.IsEditing.Should().BeFalse();
    }
}
