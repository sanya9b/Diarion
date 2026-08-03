using System.Linq;
using Diarion.Models;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class IndexableTextComposerTests
{
    [Fact]
    public void ComposeEntry_KeepsOnlyTextTheUserTyped()
    {
        var entry = new DiaryEntry
        {
            Gratitude = "за каву",
            SleepQuality = 5,
            HealthStatus = 4,
            Emotion = Emotion.Happy,
            AiSummary = "згенерований підсумок",
        };

        var segments = IndexableTextComposer.ComposeEntry(entry);

        segments.Should().Equal("за каву");
        // Ratings and mood are searchable as numbers through statistics; as prose they would let a
        // question about feelings match on a sleep score. AiSummary is our own output, not input.
        segments.Should().NotContain(s => s.Contains("згенерований"));
    }

    [Fact]
    public void ComposeEntry_EmptyEntry_ProducesNothing()
    {
        IndexableTextComposer.ComposeEntry(new DiaryEntry()).Should().BeEmpty();
    }

    [Fact]
    public void ComposeEntry_TrimsAndDropsWhitespaceOnlyFields()
    {
        var entry = new DiaryEntry { Gratitude = "  за каву  ", Triggers = "   " };

        IndexableTextComposer.ComposeEntry(entry).Should().Equal("за каву");
    }

    [Fact]
    public void ComposeEntry_MealsCollapseIntoOneSegment()
    {
        // Five boxes, one thought. Separately each would earn its own vector for two words.
        var entry = new DiaryEntry
        {
            BreakfastFood = "вівсянка",
            LunchFood = "борщ",
            DinnerFood = "риба",
        };

        IndexableTextComposer.ComposeEntry(entry).Should().Equal("вівсянка, борщ, риба");
    }

    [Fact]
    public void ComposeEntry_KeepsFieldsSeparate_SoChunksNeverStraddleThem()
    {
        var entry = new DiaryEntry { Gratitude = "за каву", Triggers = "черга в банку" };

        IndexableTextComposer.ComposeEntry(entry).Should().Equal("за каву", "черга в банку");
    }

    [Fact]
    public void ComposeNote_TakesTitleAndBody_ButNotTags()
    {
        var note = new Note
        {
            Title = "Ідеї",
            Content = "купити #книга",
            Tags = { "книга" },
        };

        // The tag is denormalized from the body; indexing it too would weight the word twice for
        // having a hash in front of it.
        IndexableTextComposer.ComposeNote(note).Should().Equal("Ідеї", "купити #книга");
    }

    [Fact]
    public void ComputeHash_IsStableForTheSameContent()
    {
        var a = IndexableTextComposer.ComputeHash(new[] { "перший", "другий" });
        var b = IndexableTextComposer.ComputeHash(new[] { "перший", "другий" });

        a.Should().Be(b);
    }

    [Fact]
    public void ComputeHash_ChangesWhenTextChanges()
    {
        var before = IndexableTextComposer.ComputeHash(new[] { "спав погано" });
        var after = IndexableTextComposer.ComputeHash(new[] { "спав добре" });

        before.Should().NotBe(after);
    }

    [Fact]
    public void ComputeHash_NoticesAWordMovingBetweenFields()
    {
        // The reason the separator is a character the user cannot type: concatenating without one
        // would make these two identical, and the entry would never be re-indexed after the edit.
        var before = IndexableTextComposer.ComputeHash(new[] { "за каву", "черга" });
        var after = IndexableTextComposer.ComputeHash(new[] { "за", "каву черга" });

        before.Should().NotBe(after);
    }

    [Fact]
    public void ComputeHash_EmptyInput_IsStillDefined()
    {
        IndexableTextComposer.ComputeHash(Enumerable.Empty<string>()).Should().NotBeNullOrEmpty();
    }
}
