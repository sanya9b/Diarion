using System.Linq;
using Diarion.Models.Markdown;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The line as the field holds it — styled characters, no markup — and the cuts, joins and toggles the
/// editor makes on it. The one property everything else relies on is at the bottom: whatever the
/// editor builds, the markdown it is saved as reads back to the same thing.
/// </summary>
public class StyledTextTests
{
    private static (string, InlineStyle)[] Runs(StyledText text)
        => text.Runs.Select(r => (r.Text, r.Style)).ToArray();

    [Fact]
    public void NeighboursThatLookTheSameAreOneRun()
    {
        var text = StyledText.FromRuns([new InlineSpan("a", InlineStyle.Bold), new InlineSpan("b", InlineStyle.Bold), new InlineSpan("", InlineStyle.Italic)]);

        Runs(text).Should().Equal(("ab", InlineStyle.Bold));
    }

    [Fact]
    public void SlicingCutsThroughRunsAndKeepsTheirStyle()
    {
        var text = StyledText.Parse("купити **молоко** сьогодні");

        Runs(text.Slice(3, 10)).Should().Equal(("ити ", InlineStyle.None), ("мол", InlineStyle.Bold));
        text.Slice(0, text.Length).Should().BeSameAs(text);
        text.Slice(4, 4).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void JoiningPutsTheRunsEndToEnd()
    {
        var joined = StyledText.Parse("**a**").Append("\n").Concat(StyledText.Parse("**b**"));

        Runs(joined).Should().Equal(("a", InlineStyle.Bold), ("\n", InlineStyle.None), ("b", InlineStyle.Bold));
        joined.Lines().Should().HaveCount(2);
    }

    [Fact]
    public void TheNextCharacterInheritsTheLookOfTheOneBeforeTheCaret()
    {
        var text = StyledText.Parse("**ab**c");

        text.StyleBefore(0).Should().Be(InlineStyle.None);
        text.StyleBefore(2).Should().Be(InlineStyle.Bold);
        text.StyleBefore(3).Should().Be(InlineStyle.None);
        text.StyleBefore(99).Should().Be(InlineStyle.None);
    }

    [Fact]
    public void ToggleAddsUnlessTheWholeRangeAlreadyHasIt()
    {
        var text = StyledText.Parse("**сло**во");

        var allBold = text.Toggle(0, 5, InlineStyle.Bold);
        Runs(allBold).Should().Equal(("слово", InlineStyle.Bold));

        var plain = allBold.Toggle(0, 5, InlineStyle.Bold);
        Runs(plain).Should().Equal(("слово", InlineStyle.None));
    }

    [Fact]
    public void ToggleLeavesTheOtherStylesAlone()
    {
        var text = StyledText.Parse("***слово***").Toggle(0, 5, InlineStyle.Bold);

        Runs(text).Should().Equal(("слово", InlineStyle.Italic));
    }

    [Fact]
    public void AnEmptyRangeChangesNothing()
    {
        var text = StyledText.Parse("слово");

        text.Toggle(2, 0, InlineStyle.Bold).Should().BeSameAs(text);
        text.HasStyle(2, 0, InlineStyle.None).Should().BeFalse();
    }

    [Fact]
    public void TypedMarkupExpandsRunByRunAndKeepsWhatTheRunAlreadyHad()
    {
        var text = StyledText.FromRuns([new InlineSpan("a **b** ", InlineStyle.Italic), new InlineSpan("`c`")]);

        text.ContainsMarkup.Should().BeTrue();
        Runs(text.ExpandMarkup()).Should().Equal(
            ("a ", InlineStyle.Italic),
            ("b", InlineStyle.Italic | InlineStyle.Bold),
            (" ", InlineStyle.Italic),
            ("c", InlineStyle.Code));
    }

    [Fact]
    public void CodeAndLinkTitlesAreNeverReReadAsMarkup()
    {
        var text = StyledText.FromRuns([new InlineSpan("a **b**", InlineStyle.Code), new InlineSpan("**c**", InlineStyle.Link)]);

        text.ContainsMarkup.Should().BeFalse();
        text.ExpandMarkup().Should().BeSameAs(text);
    }

    [Fact]
    public void EqualityIsAboutTheCharactersAndTheirLook()
    {
        StyledText.Parse("**a**b").Should().Be(StyledText.FromRuns([new InlineSpan("a", InlineStyle.Bold), new InlineSpan("b")]));
        StyledText.Parse("**a**b").Should().NotBe(StyledText.Parse("a**b**"));
        StyledText.Parse("ab").Should().NotBe(StyledText.Parse("**ab**"));
    }

    // --- composing back to markdown ---

    [Theory]
    [InlineData("**жирний**")]
    [InlineData("*курсив*")]
    [InlineData("~~закреслено~~")]
    [InlineData("***обидва***")]
    [InlineData("`код`")]
    [InlineData("[[Інша нотатка]]")]
    [InlineData("купити **молоко** і ~~каву~~ до [[Чаю]]")]
    [InlineData("~~**a**~~")]
    [InlineData("перший **a**\nдругий *b*")]
    [InlineData("2 * 3 * 4")]
    [InlineData("файл some_long_name.txt")]
    [InlineData("")]
    public void WhatWasStoredIsStoredAgainUnchanged(string markdown)
    {
        MarkdownParser.ComposeInline(StyledText.Parse(markdown)).Should().Be(markdown);
    }

    [Fact]
    public void NestedMarkupIsRewrittenFlatAndEveryLetterKeepsItsLook()
    {
        // "**жирний з `кодом`**" is one bold run and one bold-code run; the composer writes each run
        // on its own. The space between them steps outside the markers on the way — the parser will
        // not read "з **" — so it comes back plain, which no eye can tell. Every letter keeps its look.
        var text = StyledText.Parse("**жирний з `кодом`**");

        var markdown = MarkdownParser.ComposeInline(text);
        var back = StyledText.Parse(markdown);

        markdown.Should().Be("**жирний з** **`кодом`**");
        back.Text.Should().Be(text.Text);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text.Text[i])) continue;
            back.StyleAt(i).Should().Be(text.StyleAt(i), $"character {i} was {text.StyleAt(i)}");
        }
    }

    [Fact]
    public void EveryCombinationOfStylesReadsBackToItself()
    {
        // The parser is a left-to-right scanner with a fixed marker order; the composer has to write
        // in the order it scans. This walks every combination of the three emphasis styles on a run
        // and every pair of them side by side, which is exactly where two scanners would disagree.
        var styles = new[]
        {
            InlineStyle.None, InlineStyle.Bold, InlineStyle.Italic, InlineStyle.Strikethrough,
            InlineStyle.Bold | InlineStyle.Italic, InlineStyle.Bold | InlineStyle.Strikethrough,
            InlineStyle.Italic | InlineStyle.Strikethrough,
            InlineStyle.Bold | InlineStyle.Italic | InlineStyle.Strikethrough,
            InlineStyle.Code, InlineStyle.Bold | InlineStyle.Code, InlineStyle.Link
        };

        foreach (var first in styles)
        {
            foreach (var second in styles)
            {
                if (first == second) continue;

                var text = StyledText.FromRuns([new InlineSpan("ab", first), new InlineSpan("cd", second)]);
                var markdown = MarkdownParser.ComposeInline(text);

                StyledText.Parse(markdown).Should().Be(text, $"{first} then {second} was written as {markdown}");
            }
        }
    }

    [Fact]
    public void WhitespaceAtTheEdgesOfARunIsLeftOutsideTheMarkers()
    {
        // The parser refuses "** bold**", on purpose — that is what keeps "2 * 3" arithmetic — so the
        // composer keeps the spaces on the outside, where they do not have to be read at all.
        var text = StyledText.FromRuns([new InlineSpan("a", InlineStyle.None), new InlineSpan(" b ", InlineStyle.Bold), new InlineSpan("c")]);

        var markdown = MarkdownParser.ComposeInline(text);

        markdown.Should().Be("a **b** c");
        StyledText.Parse(markdown).Text.Should().Be("a b c");
    }

    [Fact]
    public void ARunOfNothingButSpacesKeepsNoStyle()
    {
        var text = StyledText.FromRuns([new InlineSpan("a"), new InlineSpan("  ", InlineStyle.Bold), new InlineSpan("b")]);

        MarkdownParser.ComposeInline(text).Should().Be("a  b");
    }

    [Fact]
    public void AMarkerPairNeverSpansALineBreak()
    {
        var text = StyledText.FromRuns([new InlineSpan("a\nb", InlineStyle.Bold)]);

        MarkdownParser.ComposeInline(text).Should().Be("**a**\n**b**");
    }
}
