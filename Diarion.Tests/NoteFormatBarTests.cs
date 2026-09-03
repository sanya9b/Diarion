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
    public void BoldTakesWhatIsSelectedAndNothingElse()
    {
        var editor = Loaded("слово тут");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleStyle(InlineStyle.Bold);

        editor.Blocks[0].Body.Runs.Select(r => (r.Text, r.Style)).Should().Equal(
            ("слово", InlineStyle.Bold),
            (" тут", InlineStyle.None));
        editor.Compose().Should().Be("**слово** тут");
    }

    [Fact]
    public void BoldOnTextThatIsAlreadyBoldTakesItOff()
    {
        // The line holds the word as bold, not as stars, so the selection is the word itself.
        var editor = Loaded("**слово** тут");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleStyle(InlineStyle.Bold);

        editor.Compose().Should().Be("слово тут");
    }

    [Fact]
    public void AHalfBoldSelectionBecomesAllBoldRatherThanAllPlain()
    {
        // The button reads as "make this bold" until every character already is; the other reading
        // would make a drag that overshot the word undo it.
        var editor = Loaded("**сло**во");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleStyle(InlineStyle.Bold);

        editor.Compose().Should().Be("**слово**");
    }

    [Fact]
    public void StylesStackInsteadOfReplacingEachOther()
    {
        var editor = Loaded("**слово**");
        Focus(editor.Blocks[0], caret: 0, length: 5);

        editor.ToggleStyle(InlineStyle.Italic);

        editor.Blocks[0].Body.Runs.Should().ContainSingle()
            .Which.Style.Should().Be(InlineStyle.Bold | InlineStyle.Italic);
        editor.Compose().Should().Be("***слово***");
    }

    [Fact]
    public void TheSelectionNeverSeesAMarker()
    {
        // The whole point: the field holds "слово", and nothing in it moves when it turns bold, so the
        // selection the user made is still the selection after the press.
        var editor = Loaded("два слова");
        Focus(editor.Blocks[0], caret: 4, length: 5);

        editor.ToggleStyle(InlineStyle.Strikethrough);

        editor.Blocks[0].Text.Should().Be("два слова");
        editor.Blocks[0].SelectionStart.Should().Be(4);
        editor.Blocks[0].SelectionLength.Should().Be(5);
    }

    [Fact]
    public void WithNothingSelectedTheButtonSetsWhatIsTypedNext()
    {
        // Press bold, type, and the word arrives bold — the way every rich text editor works. The
        // stored note does not change until a character is typed, so no save is raised either.
        var editor = Loaded("два ");
        Focus(editor.Blocks[0], caret: 4);
        var saved = false;
        editor.Changed += (_, _) => saved = true;

        editor.ToggleStyle(InlineStyle.Bold);

        editor.Blocks[0].TypingStyle.Should().Be(InlineStyle.Bold);
        editor.Compose().Should().Be("два ");
        saved.Should().BeFalse();
    }

    [Fact]
    public void PressingTheSameButtonAgainTurnsTheTypingStyleBackOff()
    {
        var editor = Loaded("два ");
        Focus(editor.Blocks[0], caret: 4);

        editor.ToggleStyle(InlineStyle.Bold);
        editor.ToggleStyle(InlineStyle.Italic);
        editor.ToggleStyle(InlineStyle.Bold);

        editor.Blocks[0].TypingStyle.Should().Be(InlineStyle.Italic);
    }

    [Fact]
    public void StylingASelectionSavesTheNote()
    {
        var editor = Loaded("слово");
        Focus(editor.Blocks[0], caret: 0, length: 5);
        var saved = false;
        editor.Changed += (_, _) => saved = true;

        editor.ToggleStyle(InlineStyle.Italic);

        saved.Should().BeTrue();
        editor.Compose().Should().Be("*слово*");
    }

    [Fact]
    public void ABoldWordSurvivesTheLineBecomingAListItem()
    {
        // Every cut the editor makes is on styled text, not on strings: the bullet button must not
        // cost the user the emphasis they just applied.
        var editor = Loaded("купити **молоко** сьогодні");
        Focus(editor.Blocks[0], caret: 3);

        editor.ToggleKind(MarkdownBlockKind.Bullet);

        editor.Compose().Should().Be("- купити **молоко** сьогодні");
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
        editor.ToggleStyle(InlineStyle.Bold);

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Checklist);
    }
}
