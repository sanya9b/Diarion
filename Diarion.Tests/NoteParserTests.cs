using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class NoteParserTests
{
    [Fact]
    public void ExtractTags_ReturnsDistinctLowercased_IgnoresCSharpAndNumericHash()
    {
        var content = "Buy milk #Groceries, review C# code, item #1, plan #groceries again #work-item";

        var tags = NoteParser.ExtractTags(content);

        tags.Should().Equal("groceries", "work-item"); // "C#" and "#1" excluded, dedup case-insensitive
    }

    [Fact]
    public void ExtractTags_EmptyOrNull_ReturnsEmpty()
    {
        NoteParser.ExtractTags(null).Should().BeEmpty();
        NoteParser.ExtractTags("").Should().BeEmpty();
        NoteParser.ExtractTags("no tags here").Should().BeEmpty();
    }

    [Fact]
    public void ExtractLinks_ReturnsNormalizedDistinct_SkipsEmpty()
    {
        var content = "See [[Note B]] and [[ note b ]] and [[Another]] and [[]] end";

        var links = NoteParser.ExtractLinks(content);

        links.Should().Equal("note b", "another"); // normalized + dedup; empty [[]] skipped
    }

    [Fact]
    public void ExtractLinkDisplayTitles_KeepsOriginalCasing_DistinctByNormalized()
    {
        var content = "Link [[Note B]] then [[note b]] then [[Third One]]";

        var display = NoteParser.ExtractLinkDisplayTitles(content);

        display.Should().Equal("Note B", "Third One"); // first-seen casing wins
    }

    [Theory]
    [InlineData("  Hello World  ", "hello world")]
    [InlineData("MixedCase", "mixedcase")]
    [InlineData(null, "")]
    public void NormalizeTitle_TrimsAndLowercases(string? input, string expected)
    {
        NoteParser.NormalizeTitle(input).Should().Be(expected);
    }
}
