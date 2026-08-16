using System.Linq;
using Diarion.Models.Markdown;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The note body while it is being typed into. Every test here drives it the way the screen does —
/// by writing into a block's <c>Text</c>, exactly as the bound editor would — and then asks what the
/// note would be saved as.
/// </summary>
public class NoteBlockEditorTests
{
    private static NoteBlockEditor Loaded(string content)
    {
        var editor = new NoteBlockEditor();
        editor.Load(content);
        return editor;
    }

    /// <summary>A newline arriving in a block is what Enter looks like from here.</summary>
    private static void PressEnter(NoteBlockViewModel block, int at) => block.Text = block.Text.Insert(at, "\n");

    [Fact]
    public void ANewNoteHasOneEmptyLineToTypeInto()
    {
        var editor = new NoteBlockEditor();

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Paragraph);
        editor.Compose().Should().BeEmpty();
    }

    // --- the marker disappearing as it is typed ---

    [Fact]
    public void TypingATickBoxMarkerLeavesATickBoxAndNoMarker()
    {
        var editor = Loaded(string.Empty);

        editor.Blocks[0].Text = "[] ";

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Checklist);
        editor.Blocks[0].Text.Should().BeEmpty();
        editor.Blocks[0].IsFocusRequested.Should().BeTrue("the caret has to survive the block being swapped");
    }

    [Fact]
    public void AMarkerTypedMidNoteCutsTheProseAroundIt()
    {
        var editor = Loaded("вступ\nпісляслово");

        editor.Blocks[0].Text = "вступ\n- \nпісляслово";

        editor.Blocks.Select(b => b.Kind).Should().Equal(
            MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet, MarkdownBlockKind.Paragraph);
        editor.Compose().Should().Be("вступ\n- \nпісляслово");
    }

    [Fact]
    public void ABulletBecomesATickBoxWhenTheBracketsAreTyped()
    {
        // How you actually get a tick box: "- " makes a bullet first, then "[]" is typed into it.
        var editor = Loaded("- ");

        editor.Blocks[0].Text = "[]";

        editor.Blocks[0].Kind.Should().Be(MarkdownBlockKind.Checklist);
        editor.Compose().Should().Be("- [ ] ");
    }

    [Fact]
    public void APastedListArrivesAsAListRatherThanAsItsSourceText()
    {
        var editor = Loaded(string.Empty);

        editor.Blocks[0].Text = "- хліб\n- молоко\n- кава";

        editor.Blocks.Should().HaveCount(3).And.OnlyContain(b => b.Kind == MarkdownBlockKind.Bullet);
        editor.Compose().Should().Be("- хліб\n- молоко\n- кава");
    }

    [Fact]
    public void OrdinaryProseIsLeftCompletelyAlone()
    {
        // The expensive mistake here would be reshaping the list on every keystroke: the editor the
        // user is typing in would be destroyed and rebuilt under their fingers.
        var editor = Loaded("просто текст");

        editor.Blocks[0].Text = "просто текст!";

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].IsFocusRequested.Should().BeFalse();
        editor.Compose().Should().Be("просто текст!");
    }

    // --- Enter ---

    [Fact]
    public void EnterInAListGivesAnotherItemOfTheSameKind()
    {
        var editor = Loaded("- [ ] хліб");

        PressEnter(editor.Blocks[0], "хліб".Length);

        editor.Blocks.Should().HaveCount(2).And.OnlyContain(b => b.Kind == MarkdownBlockKind.Checklist);
        editor.Blocks[1].IsChecked.Should().BeFalse("a fresh item starts unticked whatever the one above it did");
        editor.Compose().Should().Be("- [ ] хліб\n- [ ] ");
    }

    [Fact]
    public void EnterInTheMiddleOfAnItemSplitsIt()
    {
        var editor = Loaded("- хлібмолоко");

        PressEnter(editor.Blocks[0], "хліб".Length);

        editor.Compose().Should().Be("- хліб\n- молоко");
    }

    [Fact]
    public void EnterOnAnEmptyItemTakesTheMarkerOffInsteadOfAddingAnother()
    {
        // The way out of a list, and the only one there is: there is no key event to hang it on, so
        // Enter on an item with nothing in it has to mean "done".
        var editor = Loaded("- [ ] хліб\n- [ ] ");

        PressEnter(editor.Blocks[1], 0);

        editor.Blocks.Select(b => b.Kind).Should().Equal(
            MarkdownBlockKind.Checklist, MarkdownBlockKind.Paragraph);
        editor.Compose().Should().Be("- [ ] хліб\n");
    }

    [Fact]
    public void EnterAfterAHeadingDropsYouIntoOrdinaryText()
    {
        var editor = Loaded("# Покупки\nтекст");

        PressEnter(editor.Blocks[0], "Покупки".Length);

        editor.Blocks.Select(b => b.Kind).Should().Equal(
            MarkdownBlockKind.Heading1, MarkdownBlockKind.Paragraph);
        editor.Compose().Should().Be("# Покупки\n\nтекст");
    }

    [Fact]
    public void ProseThatEndsUpNextToProseBecomesOneBlockAgain()
    {
        // Two runs side by side would leave a blank line stranded in a block of its own, where
        // backspace could never reach it. The caret has to follow the join.
        var editor = Loaded("# Покупки\nтекст");

        PressEnter(editor.Blocks[0], "Покупки".Length);

        editor.Blocks.Should().HaveCount(2);
        editor.Blocks[1].Text.Should().Be("\nтекст");
        editor.Blocks[1].IsFocusRequested.Should().BeTrue();
        editor.Blocks[1].Caret.Should().Be(0, "the caret stays on the new empty line, not at the end of the old text");
    }

    // --- numbering ---

    [Fact]
    public void InsertingAnItemRenumbersTheOnesBelowIt()
    {
        var editor = Loaded("1. перше\n2. друге");

        PressEnter(editor.Blocks[0], "перше".Length);

        editor.Blocks.Select(b => b.Number).Should().Equal(1, 2, 3);
        editor.Compose().Should().Be("1. перше\n2. \n3. друге");
    }

    // --- ticking ---

    [Fact]
    public void TickingABoxChangesTheStoredMarkerAndNothingElse()
    {
        var editor = Loaded("- [ ] молоко");

        editor.Blocks[0].ToggleCheckCommand.Execute(null);

        editor.Compose().Should().Be("- [x] молоко");
        editor.Blocks.Should().ContainSingle();
    }

    [Fact]
    public void EveryEditIsAnnouncedSoTheNoteGetsSaved()
    {
        var editor = Loaded("- [ ] молоко");
        var announced = 0;
        editor.Changed += (_, _) => announced++;

        editor.Blocks[0].Text = "молоко 2л";
        editor.Blocks[0].ToggleCheckCommand.Execute(null);

        announced.Should().Be(2);
    }

    // --- the tap on the empty space below the note ---

    [Fact]
    public void TappingBelowAListAddsALineToLandOn()
    {
        var editor = Loaded("- [ ] хліб");

        editor.FocusEnd();

        editor.Blocks.Should().HaveCount(2);
        editor.Blocks[1].Kind.Should().Be(MarkdownBlockKind.Paragraph);
        editor.Blocks[1].IsFocusRequested.Should().BeTrue();
    }

    [Fact]
    public void TappingBelowProseJustPutsTheCaretAtItsEnd()
    {
        var editor = Loaded("текст");

        editor.FocusEnd();

        editor.Blocks.Should().ContainSingle();
        editor.Blocks[0].Caret.Should().Be("текст".Length);
    }

    // --- the placeholder ---

    [Fact]
    public void OnlyAnEmptyNoteInvitesYouToWriteSomething()
    {
        var editor = Loaded(string.Empty);

        editor.Blocks[0].ShowsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void ABlankLineInTheMiddleOfANoteSaysNothing()
    {
        // "# Покупки\n\n- хліб" holds an empty paragraph between the heading and the list. Prompting
        // the user to write something there would put the hint in the middle of a written note.
        var editor = Loaded("# Покупки\n\n- хліб");

        editor.Blocks.Should().HaveCount(3);
        editor.Blocks.Should().OnlyContain(b => !b.ShowsPlaceholder);
    }

    [Fact]
    public void TheInvitationGoesAwayAsSoonAsThereIsANote()
    {
        var editor = Loaded(string.Empty);

        editor.Blocks[0].Text = "- ";

        editor.Blocks[0].ShowsPlaceholder.Should().BeFalse();
    }

    // --- loading ---

    [Fact]
    public void LoadingReplacesEverythingAndSaysNothing()
    {
        var editor = Loaded("- перше");
        var announced = 0;
        editor.Changed += (_, _) => announced++;

        editor.Load("# Друге");

        editor.Blocks.Should().ContainSingle();
        editor.Compose().Should().Be("# Друге");
        announced.Should().Be(0, "opening a note is not an edit of it");
    }
}
