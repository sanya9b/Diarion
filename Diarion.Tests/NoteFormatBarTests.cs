using System.Linq;
using Diarion.Models.Markdown;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The formatting bar under the note editor. It reaches the same code as typing "- " or "**" does —
/// these tests are about the half that typing never had to answer: which line is being formatted, and
/// which stretch of it.
/// </summary>
public class NoteFormatBarTests
{
    private static NoteBlockEditor Loaded(string content)
    {
        var editor = new NoteBlockEditor();
        editor.Load(content);
        return editor;
    }

    /// <summary>What the bound field does when the user taps into a line and puts the caret somewhere.</summary>
    private static NoteBlockViewModel Focus(NoteBlockViewModel block, int caret = 0, int length = 0)
    {
        block.HoldEditCommand.Execute(null);
        block.SelectionStart = caret;
        block.SelectionLength = length;
        return block;
    }

    // --- whole lines ---

    [Fact]
    public void TheListButtonTakesTheLineTheCaretIsInAndNotTheWholeRun()
    {
        // Prose is held as one block however many lines it has, so "make this a bullet" has to cut
        // one line out of the middle of it. The caret is the only thing that says which.
        var editor = Loaded("рядок один\nрядок два\nрядок три");
        Focus(editor.Blocks[0], caret: "рядок один\n".Length);

        editor.ToggleKind(MarkdownBlockKind.Bullet);

        editor.Blocks.Select(b => b.Kind).Should().Equal(
            MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet, MarkdownBlockKind.Paragraph);
        editor.Compose().Should().Be("рядок один\n- рядок два\nрядок три");
    }

    [Fact]
    public void TheSameButtonPressedAgainTakesTheListOff()
    {
        var editor = Loaded("- хліб");
        Focus(editor.Blocks[0]);

        editor.ToggleKind(MarkdownBlockKind.Bullet);

        editor.Blocks.Should().ContainSingle();
        editor.Compose().Should().Be("хліб");
    }

    [Fact]
    public void ALineThatGoesBackToProseJoinsTheProseAroundIt()
    {
        // Otherwise the note would keep a seam nothing on screen explains: two runs where there is
        // one paragraph, and a backspace at the join that deletes nothing.
        var editor = Loaded("вступ\n- список\nпісляслово");
        Focus(editor.Blocks[1]);

        editor.ToggleKind(MarkdownBlockKind.Bullet);

        editor.Blocks.Should().ContainSingle();
        editor.Compose().Should().Be("вступ\nсписок\nпісляслово");
    }

    [Fact]
    public void OneButtonWalksTheThreeHeadingSizesAndThenBackToPlainText()
    {
        // Three buttons for three sizes would be a third of the bar spent on something the user can
        // see on screen after one press.
        var editor = Loaded("Заголовок");
        Focus(editor.Blocks[0]);

        editor.CycleHeading();
        editor.Compose().Should().Be("# Заголовок");

        editor.CycleHeading();
        editor.Compose().Should().Be("## Заголовок");

        editor.CycleHeading();
        editor.Compose().Should().Be("### Заголовок");

        editor.CycleHeading();
        editor.Compose().Should().Be("Заголовок");
    }

    [Fact]
    public void ListsMadeWithTheButtonAreNumberedInOrder()
    {
        var editor = Loaded("перший\nдругий");
        Focus(editor.Blocks[0]);
        editor.ToggleKind(MarkdownBlockKind.Numbered);

        Focus(editor.Blocks[1]);
        editor.ToggleKind(MarkdownBlockKind.Numbered);

        editor.Compose().Should().Be("1. перший\n2. другий");
    }

    [Fact]
    public void ATickBoxMadeWithTheButtonKeepsTheTextItWasMadeFrom()
    {
        var editor = Loaded("купити хліб");
        Focus(editor.Blocks[0], caret: 3);

        editor.ToggleKind(MarkdownBlockKind.Checklist);

        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Checklist);
        editor.Compose().Should().Be("- [ ] купити хліб");
    }

    [Fact]
    public void FormattingSavesTheNote()
    {
        // The bar writes to the same blocks the keyboard does, so it has to raise the same event —
        // a note formatted and then closed must not lose the formatting.
        var editor = Loaded("цитата");
        Focus(editor.Blocks[0]);

        var saved = false;
        editor.Changed += (_, _) => saved = true;

        editor.ToggleKind(MarkdownBlockKind.Quote);

        saved.Should().BeTrue();
        editor.Compose().Should().Be("> цитата");
    }

    [Fact]
    public void TheCaretLandsBackInTheLineThatWasJustFormatted()
    {
        var editor = Loaded("рядок");
        Focus(editor.Blocks[0]);

        editor.ToggleKind(MarkdownBlockKind.Bullet);

        editor.Blocks[0].IsFocusRequested.Should().BeTrue("the block was swapped for one of the new kind");
        editor.Blocks[0].Caret.Should().Be("рядок".Length);
    }

    // --- stretches of text ---

    [Fact]
    public void BoldWrapsWhatIsSelected()
    {
        var editor = Loaded("слово тут");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleInline("**");

        editor.Compose().Should().Be("**слово** тут");
    }

    [Fact]
    public void BoldOnTextThatIsAlreadyBoldTakesItOff()
    {
        var editor = Loaded("**слово** тут");
        Focus(editor.Blocks[0], caret: 2, length: 5);

        editor.ToggleInline("**");

        editor.Compose().Should().Be("слово тут");
    }

    [Fact]
    public void AWordSelectedWithItsStarsStillTogglesRatherThanGainingMore()
    {
        // A double tap on a phone often takes the markers with the word. Adding a second pair would
        // be the one outcome nobody asked for.
        var editor = Loaded("~~слово~~ тут");
        Focus(editor.Blocks[0], caret: 0, length: "~~слово~~".Length);

        editor.ToggleInline("~~");

        editor.Compose().Should().Be("слово тут");
    }

    [Fact]
    public void WithNothingSelectedTheButtonTakesTheWordTheCaretIsIn()
    {
        // Selecting a word first is two gestures on a phone, and a button that inserted an empty
        // pair of markers would be a button asking the user to type between them.
        var editor = Loaded("два слова");
        Focus(editor.Blocks[0], caret: 6);

        editor.ToggleInline("~~");

        editor.Compose().Should().Be("два ~~слова~~");
    }

    [Fact]
    public void InWhitespaceTheMarkersGoInAndTheCaretGoesBetweenThem()
    {
        var editor = Loaded("два ");
        Focus(editor.Blocks[0], caret: 4);

        editor.ToggleInline("**");

        editor.Compose().Should().Be("два ****");
        editor.Blocks[0].Caret.Should().Be(6, "the next thing typed belongs between the stars");
    }

    [Fact]
    public void ItalicDoesNotUnpickBold()
    {
        // "*" sitting against another "*" is half of "**". Reading it as an italic marker would turn
        // pressing italic into a way of removing bold.
        var editor = Loaded("**слово**");
        Focus(editor.Blocks[0], caret: 2, length: 5);

        editor.ToggleInline("*");

        editor.Compose().Should().Be("***слово***");
    }

    [Fact]
    public void FormattingKeepsTheLineAsAFieldRatherThanLettingItBecomeALabel()
    {
        // A line with markup is drawn as a formatted label when it is not being edited. Swapping the
        // line under the caret for a label is not what pressing "bold" asks for.
        var editor = Loaded("слово");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleInline("**");

        editor.Blocks[0].IsEditing.Should().BeTrue();
        editor.Blocks[0].ShowsRawText.Should().BeTrue();
    }

    // --- which line the bar is aiming at ---

    [Fact]
    public void TheBarActsOnTheLineTheCaretLeftRatherThanOnNothing()
    {
        // Pressing a button is exactly the moment focus can leave the text — it does on Windows. A
        // target cleared on blur would be null in the only case that matters.
        var editor = Loaded("перший\n- другий");
        Focus(editor.Blocks[1]);
        editor.Blocks[1].EndEditCommand.Execute(null);

        editor.ToggleKind(MarkdownBlockKind.Quote);

        editor.Compose().Should().Be("перший\n> другий");
    }

    [Fact]
    public void WithoutTheCaretEverLandingAnywhereTheBarTakesTheLastLine()
    {
        // The note has been opened and not yet typed into, and the bar is on screen: it has to do
        // something, and the end of the note is where typing would have started.
        var editor = Loaded("перший\n- другий");

        editor.ToggleKind(MarkdownBlockKind.Quote);

        editor.Compose().Should().Be("перший\n> другий");
    }

    [Fact]
    public void AnEmptyNoteSurvivesTheButtonsBeingPressedAtIt()
    {
        var editor = new NoteBlockEditor();

        editor.ToggleKind(MarkdownBlockKind.Checklist);
        editor.ToggleInline("**");

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Checklist);
    }
}
