using System.Linq;
using Diarion.Models.Markdown;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The recogniser behind the note editor. Everything here is a pure string-to-blocks question, and
/// every one of these cases is a way a note could be silently mangled: a tag read as a heading, a
/// tick lost on save, a hand-nested list flattened.
/// </summary>
public class MarkdownParserTests
{
    // --- what a line turns out to be ---

    [Theory]
    [InlineData("# Покупки", MarkdownBlockKind.Heading1, "Покупки")]
    [InlineData("## Тиждень", MarkdownBlockKind.Heading2, "Тиждень")]
    [InlineData("### Понеділок", MarkdownBlockKind.Heading3, "Понеділок")]
    [InlineData("- хліб", MarkdownBlockKind.Bullet, "хліб")]
    [InlineData("* хліб", MarkdownBlockKind.Bullet, "хліб")]
    [InlineData("+ хліб", MarkdownBlockKind.Bullet, "хліб")]
    [InlineData("1. перше", MarkdownBlockKind.Numbered, "перше")]
    [InlineData("2) друге", MarkdownBlockKind.Numbered, "друге")]
    [InlineData("> цитата", MarkdownBlockKind.Quote, "цитата")]
    [InlineData("звичайний рядок", MarkdownBlockKind.Paragraph, "звичайний рядок")]
    public void EachMarkerIsRecognisedAndTakenOffTheText(string line, MarkdownBlockKind kind, string text)
    {
        var block = MarkdownParser.ParseBlocks(line).Single();

        block.Kind.Should().Be(kind);
        block.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("- [ ] хліб", false, "хліб")]
    [InlineData("- [x] молоко", true, "молоко")]
    [InlineData("- [X] молоко", true, "молоко")]
    [InlineData("[] хліб", false, "хліб")]
    [InlineData("[x] молоко", true, "молоко")]
    public void ATickBoxIsRecognisedWithOrWithoutItsDash(string line, bool ticked, string text)
    {
        var block = MarkdownParser.ParseBlocks(line).Single();

        block.Kind.Should().Be(MarkdownBlockKind.Checklist);
        block.IsChecked.Should().Be(ticked);
        block.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("#покупки")]          // a tag, and tags are older than this feature
    [InlineData("####### забагато")]  // seven levels is not a heading in any dialect
    [InlineData("C# і трохи тексту")]
    public void AHashWithoutASpaceIsNotAHeading(string line)
    {
        MarkdownParser.ParseBlocks(line).Single().Kind.Should().Be(MarkdownBlockKind.Paragraph);
    }

    [Theory]
    [InlineData("[[Інша нотатка]]")]
    [InlineData("- [[Інша нотатка]]")]
    public void ALinkedNoteIsNotATickBox(string line)
    {
        // Two brackets in a row is the older syntax and it wins: exactly one character may sit
        // between the brackets of a tick box.
        MarkdownParser.ParseBlocks(line).Single().Kind.Should().NotBe(MarkdownBlockKind.Checklist);
    }

    [Fact]
    public void AQuoteNeedsItsSpaceToo()
    {
        // Otherwise the line would jump into a quote the instant someone typed a lone ">".
        MarkdownParser.ParseBlocks(">цитата").Single().Kind.Should().Be(MarkdownBlockKind.Paragraph);
    }

    // --- how lines are grouped ---

    [Fact]
    public void ProseLinesAreHeldAsOneRun()
    {
        // One block, not four: inside a run Enter, wrapping and backspace stay the platform's own.
        var blocks = MarkdownParser.ParseBlocks("перший\nдругий\n\nчетвертий");

        blocks.Should().ContainSingle();
        blocks[0].Kind.Should().Be(MarkdownBlockKind.Paragraph);
        blocks[0].Text.Should().Be("перший\nдругий\n\nчетвертий");
    }

    [Fact]
    public void AMarkedLineBreaksTheRunInTwo()
    {
        var blocks = MarkdownParser.ParseBlocks("вступ\n- хліб\nпісляслово");

        blocks.Select(b => b.Kind).Should().Equal(
            MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet, MarkdownBlockKind.Paragraph);
        blocks[0].Text.Should().Be("вступ");
        blocks[2].Text.Should().Be("післяслово");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyNoteStillHasSomethingToTypeInto(string? content)
    {
        MarkdownParser.ParseBlocks(content).Should().ContainSingle()
            .Which.Text.Should().BeEmpty();
    }

    [Fact]
    public void EveryFlavourOfLineEndingIsUnderstood()
    {
        MarkdownParser.ParseBlocks("- один\r\n- два\r- три")
            .Should().HaveCount(3).And.OnlyContain(b => b.Kind == MarkdownBlockKind.Bullet);
    }

    // --- numbering ---

    [Fact]
    public void ANumberedListIsCountedRatherThanRead()
    {
        // Whatever numbers are in the text, the list reads 1, 2, 3 — which is also what makes
        // inserting an item in the middle harmless.
        var blocks = MarkdownParser.ParseBlocks("5. перше\n5. друге\n9. третє");

        blocks.Select(b => b.Number).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void AnythingElseStartsTheCountAgain()
    {
        var blocks = MarkdownParser.ParseBlocks("1. перше\nтекст\n1. знову перше");

        blocks.Select(b => b.Number).Should().Equal(1, 0, 1);
    }

    // --- there and back ---

    [Theory]
    [InlineData("")]
    [InlineData("просто текст")]
    [InlineData("перший\nдругий\n\nчетвертий")]
    [InlineData("# Покупки\n\n- [ ] хліб\n- [x] молоко\n- кава")]
    [InlineData("## План\n1. перше\n2. друге\n\n> цитата\n\nхвіст #тег і [[Лінк]]")]
    [InlineData("  - вкладений")]
    public void ANoteComesBackOutExactlyAsItWentIn(string content)
    {
        MarkdownParser.Compose(MarkdownParser.ParseBlocks(content)).Should().Be(content);
    }

    [Fact]
    public void TheOnlyThingARoundTripChangesIsTheSpellingOfAMarker()
    {
        // Bullets settle on "-", numbers on "1.", ticks on "- [x]". Predictable, and it means two
        // notes that read the same are stored the same.
        MarkdownParser.Compose(MarkdownParser.ParseBlocks("* хліб\n1) перше\n[x] готово"))
            .Should().Be("- хліб\n1. перше\n- [x] готово");
    }

    [Fact]
    public void AHandNestedListIsNotFlattened()
    {
        MarkdownParser.ParseBlocks("  - вкладений").Single().Indent.Should().Be("  ");
    }

    // --- the marker vanishing as it is typed ---

    [Theory]
    [InlineData("# ", MarkdownBlockKind.Heading1)]
    [InlineData("## ", MarkdownBlockKind.Heading2)]
    [InlineData("- ", MarkdownBlockKind.Bullet)]
    [InlineData("1. ", MarkdownBlockKind.Numbered)]
    [InlineData("[]", MarkdownBlockKind.Checklist)]
    [InlineData("[] ", MarkdownBlockKind.Checklist)]
    [InlineData("- [] ", MarkdownBlockKind.Checklist)]
    [InlineData("> ", MarkdownBlockKind.Quote)]
    public void TypingAMarkerIsEnoughToBecomeTheThing(string typed, MarkdownBlockKind kind)
    {
        MarkdownParser.TryPromote(typed, out var block).Should().BeTrue();
        block.Kind.Should().Be(kind);
        block.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-")]
    [InlineData("#")]
    [InlineData("1.")]
    [InlineData("")]
    [InlineData("два - три")]
    public void HalfATypedMarkerIsStillText(string typed)
    {
        MarkdownParser.TryPromote(typed, out _).Should().BeFalse();
    }

    [Fact]
    public void ARunIsCutAtTheLineThatJustBecameAList()
    {
        MarkdownParser.TrySplitRun("вступ\n- \nпісляслово", out var before, out var promoted, out var after)
            .Should().BeTrue();

        before.Should().Be("вступ");
        promoted.Kind.Should().Be(MarkdownBlockKind.Bullet);
        after.Should().Be("післяслово");
    }

    [Fact]
    public void ThereIsNoEmptyBlockOnEitherSideWhenThereIsNothingThere()
    {
        // null means "no prose on that side"; "" would mean "a blank line", and the difference is a
        // stray empty line appearing in the note every time a list starts.
        MarkdownParser.TrySplitRun("- ", out var before, out _, out var after).Should().BeTrue();

        before.Should().BeNull();
        after.Should().BeNull();
    }

    [Fact]
    public void PlainProseIsNotCutAtAll()
    {
        MarkdownParser.TrySplitRun("нічого\nтут\nнемає", out _, out _, out _).Should().BeFalse();
    }

    // --- the note as one line of readable text ---

    [Fact]
    public void ThePreviewShowsTheWordsAndNoneOfTheMarkup()
    {
        MarkdownParser.ToPlainText("# Покупки\n\n- [x] **молоко**\n- ~~кава~~ і [[Чай]]")
            .Should().Be("Покупки молоко кава і Чай");
    }

    [Fact]
    public void TheTitleIsTheFirstLineThatSaysSomething()
    {
        MarkdownParser.FirstPlainLine("\n\n## `Покупки`\nрешта").Should().Be("Покупки");
    }

    [Fact]
    public void ANoteWithNothingInItHasNoTitle()
    {
        MarkdownParser.FirstPlainLine("\n  \n").Should().BeEmpty();
    }

    [Fact]
    public void SnakeCaseIsLeftAlone()
    {
        // A lone underscore is never read as emphasis, or every file name in a note would lose its
        // middle.
        MarkdownParser.StripInline("файл some_long_name.txt").Should().Be("файл some_long_name.txt");
    }

    // --- the line cut into the stretches that are drawn differently ---

    [Fact]
    public void ALineWithNoMarkupIsOneSpanAndNothingElse()
    {
        // The common case, and the reason it is cheap: no markup means no work and no extra spans.
        var spans = MarkdownParser.ParseInline("звичайний рядок без нічого");

        spans.Should().ContainSingle();
        spans[0].Text.Should().Be("звичайний рядок без нічого");
        spans[0].Style.Should().Be(InlineStyle.None);
    }

    [Theory]
    [InlineData("**молоко**", InlineStyle.Bold)]
    [InlineData("__молоко__", InlineStyle.Bold)]
    [InlineData("*молоко*", InlineStyle.Italic)]
    [InlineData("~~молоко~~", InlineStyle.Strikethrough)]
    [InlineData("`молоко`", InlineStyle.Code)]
    [InlineData("[[молоко]]", InlineStyle.Link)]
    public void EachPairOfMarkersStylesWhatIsBetweenThemAndDisappears(string text, InlineStyle style)
    {
        var spans = MarkdownParser.ParseInline(text);

        spans.Should().ContainSingle();
        spans[0].Text.Should().Be("молоко");
        spans[0].Style.Should().Be(style);
    }

    [Fact]
    public void TheWordsAroundTheEmphasisKeepTheirOwnSpans()
    {
        var spans = MarkdownParser.ParseInline("купити **молоко** сьогодні");

        spans.Select(span => (span.Text, span.Style)).Should().Equal(
            ("купити ", InlineStyle.None),
            ("молоко", InlineStyle.Bold),
            (" сьогодні", InlineStyle.None));
    }

    [Fact]
    public void EmphasisInsideEmphasisCarriesBothStyles()
    {
        var spans = MarkdownParser.ParseInline("**жирний з `кодом`**");

        spans.Select(span => (span.Text, span.Style)).Should().Equal(
            ("жирний з ", InlineStyle.Bold),
            ("кодом", InlineStyle.Bold | InlineStyle.Code));
    }

    [Theory]
    [InlineData("2 * 3 * 4")]           // arithmetic, not italics
    [InlineData("файл some_long_name")] // a file name, not emphasis
    [InlineData("**молоко")]            // opened and never closed
    [InlineData("****")]                // nothing between the markers
    [InlineData("C# і трохи тексту")]
    public void WhatOnlyLooksLikeMarkupIsLeftAsWritten(string text)
    {
        var spans = MarkdownParser.ParseInline(text);

        spans.Should().ContainSingle();
        spans[0].Text.Should().Be(text);
        MarkdownParser.HasInlineMarkup(text).Should().BeFalse();
    }

    [Fact]
    public void ALineIsWorthDrawingAsALabelOnlyWhenSomethingWouldChange()
    {
        MarkdownParser.HasInlineMarkup("просто текст").Should().BeFalse();
        MarkdownParser.HasInlineMarkup("трохи **жирного**").Should().BeTrue();
        MarkdownParser.HasInlineMarkup("і [[Чай]]").Should().BeTrue();
    }

    [Fact]
    public void StrippingALineIsTheSameSpansWithTheStylesForgotten()
    {
        // The preview, the title and the editor all read the markup through one pair of eyes, so they
        // cannot disagree about where a word ends.
        const string line = "купити **молоко** і ~~каву~~ до [[Чаю]]";

        MarkdownParser.StripInline(line)
            .Should().Be(string.Concat(MarkdownParser.ParseInline(line).Select(span => span.Text)));
        MarkdownParser.StripInline(line).Should().Be("купити молоко і каву до Чаю");
    }
}
