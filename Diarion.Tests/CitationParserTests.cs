using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CitationParserTests
{
    private static ChatCitation Citation(int marker) => new(
        marker,
        EmbeddingSourceKind.Diary,
        $"entry-{marker}",
        new DateTime(2026, 6, marker),
        $"текст {marker}");

    private static readonly IReadOnlyList<ChatCitation> Offered = [Citation(1), Citation(2), Citation(3)];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NothingGenerated_IsARefusal(string? answer)
    {
        CitationParser.Parse(answer, Offered).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_AnswerWithNoCitations_IsDowngradedToARefusal()
    {
        // The whole guarantee. An answer citing nothing came from the model's weights rather than
        // from the diary, and no prompt instruction can rule that out — this can.
        var answer = CitationParser.Parse("Ви спали найкраще у вівторок.", Offered);

        answer.IsRefusal.Should().BeTrue();
        answer.Text.Should().BeEmpty();
        answer.Used.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ValidCitation_IsKept()
    {
        var answer = CitationParser.Parse("Найдовший сон був 2 червня [2].", Offered);

        answer.IsRefusal.Should().BeFalse();
        answer.Text.Should().Be("Найдовший сон був 2 червня [2].");
        answer.Used.Should().ContainSingle().Which.Marker.Should().Be(2);
    }

    [Fact]
    public void Parse_InventedMarker_IsDropped()
    {
        // A marker outside what was offered is a decoration, not a citation.
        var answer = CitationParser.Parse("Так було у травні [9].", Offered);

        answer.IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_MixOfRealAndInvented_KeepsOnlyTheReal()
    {
        var answer = CitationParser.Parse("Спершу [1], а потім [7].", Offered);

        answer.IsRefusal.Should().BeFalse();
        answer.Used.Select(c => c.Marker).Should().Equal(1);
    }

    [Fact]
    public void Parse_RepeatedCitation_IsListedOnce()
    {
        var answer = CitationParser.Parse("Про це [1] і ще раз про це [1].", Offered);

        answer.Used.Should().ContainSingle();
    }

    [Fact]
    public void Parse_CitationsAreInTheOrderTheAnswerUsesThem()
    {
        var answer = CitationParser.Parse("Спочатку [3], потім [1], далі [2].", Offered);

        answer.Used.Select(c => c.Marker).Should().Equal(3, 1, 2);
    }

    [Fact]
    public void Parse_ZeroMarker_IsNotACitation()
    {
        CitationParser.Parse("Нуль [0].", Offered).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_BracketsThatAreNotMarkers_AreIgnored()
    {
        CitationParser.Parse("Було [дуже] добре.", Offered).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_NothingOffered_IsAlwaysARefusal()
    {
        CitationParser.Parse("Відповідь [1].", []).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_BareMarkersWithNoProse_AreNotAnAnswer()
    {
        // Qwen3-0.6B answered two of four evaluation questions with exactly this: grounded by every
        // mechanical measure, and of no use to anyone reading it.
        CitationParser.Parse("[1]", Offered).IsRefusal.Should().BeTrue();
        CitationParser.Parse("[1], [2], [3]", Offered).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void Parse_ShortButRealSentence_IsKept()
    {
        CitationParser.Parse("Так, ви бігали вранці [1].", Offered).IsRefusal.Should().BeFalse();
    }

    [Fact]
    public void Parse_TrimsTheAnswer()
    {
        CitationParser.Parse("  Достатньо довга відповідь [1].  ", Offered).Text.Should().Be("Достатньо довга відповідь [1].");
    }
}
