using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PromptBuilderTests
{
    private static ScoredChunk Chunk(float score, string text = "запис про день", int day = 3, float[]? vector = null)
    {
        var v = vector ?? [1f, 0f];
        EmbeddingMath.NormalizeInPlace(v);
        return new ScoredChunk(
            new EmbeddingChunk
            {
                Id = $"{day}-{text.GetHashCode()}",
                SourceKind = EmbeddingSourceKind.Diary,
                SourceId = $"entry-{day}",
                SourceDate = new DateTime(2026, 6, day),
                Text = text,
                ModelId = "m",
                Dim = v.Length,
                Vector = EmbeddingMath.ToBytes(v),
            },
            score);
    }

    [Fact]
    public void Build_BlankQuestion_IsUnanswerable()
    {
        PromptBuilder.Build("   ", [Chunk(0.9f), Chunk(0.8f)]).IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public void Build_NothingRetrieved_IsUnanswerable()
    {
        PromptBuilder.Build("коли я спав найкраще", []).IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public void Build_OnlyWeakMatches_IsUnanswerable()
    {
        // Everything below the measured floor is noise, and answering from noise produces confident
        // nonsense about the user's own life.
        var prompt = PromptBuilder.Build("тривога", [Chunk(0.27f), Chunk(0.2f), Chunk(0.1f)]);

        prompt.IsAnswerable.Should().BeFalse();
        prompt.Citations.Should().BeEmpty();
    }

    [Fact]
    public void Build_ASingleGoodMatch_IsStillUnanswerable()
    {
        // One passage above the floor is a coincidence often enough to be worth refusing.
        PromptBuilder.Build("тривога", [Chunk(0.9f), Chunk(0.1f)]).IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public void Build_TwoGoodMatches_IsAnswerable()
    {
        var prompt = PromptBuilder.Build("кава", [Chunk(0.6f, "про каву", 3), Chunk(0.5f, "знову кава", 4)]);

        prompt.IsAnswerable.Should().BeTrue();
        prompt.Citations.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ExactlyAtTheFloor_Counts()
    {
        PromptBuilder.Build("щось", [Chunk(PromptBuilder.MinRelevance), Chunk(PromptBuilder.MinRelevance)])
            .IsAnswerable.Should().BeTrue();
    }

    [Fact]
    public void Build_MarkersStartAtOneAndAreContiguous()
    {
        var prompt = PromptBuilder.Build("кава", Enumerable.Range(3, 4).Select(d => Chunk(0.6f, $"день {d}", d)).ToList());

        prompt.Citations.Select(c => c.Marker).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Build_CapsThePassageCount()
    {
        var many = Enumerable.Range(1, 20)
            .Select(d => Chunk(0.6f, $"день {d}", d, [1f, d * 0.05f]))
            .ToList();

        PromptBuilder.Build("щось", many).Citations.Should().HaveCount(PromptBuilder.MaxPassages);
    }

    [Fact]
    public void Build_PrefersVarietyOverEightParaphrasesOfTheSameEvening()
    {
        // Four near-identical passages score highest; one different passage scores lower. Pure
        // relevance would fill the prompt with the first four and never mention the fifth.
        var candidates = new List<ScoredChunk>
        {
            Chunk(0.90f, "офіс один", 1, [1f, 0f]),
            Chunk(0.89f, "офіс два", 2, [1f, 0.01f]),
            Chunk(0.88f, "офіс три", 3, [1f, 0.02f]),
            Chunk(0.40f, "зовсім інше", 4, [0f, 1f]),
        };

        var prompt = PromptBuilder.Build("робота", candidates);

        prompt.Citations.Should().Contain(c => c.Text == "зовсім інше");
    }

    [Fact]
    public void Build_PassagesAreChronological()
    {
        var prompt = PromptBuilder.Build("щось", [
            Chunk(0.9f, "пізніше", 20, [1f, 0f]),
            Chunk(0.8f, "раніше", 5, [0f, 1f]),
        ]);

        prompt.Citations.Select(c => c.SourceDate).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_PromptCarriesTheDateOfEveryPassage()
    {
        // Without dates a "when did I..." question is unanswerable however good the retrieval.
        var prompt = PromptBuilder.Build("коли", [Chunk(0.6f, "подія", 14), Chunk(0.6f, "інша подія", 15)]);

        prompt.Text.Should().Contain("14");
        prompt.Text.Should().Contain("15");
    }

    [Fact]
    public void Build_PromptInstructsInUkrainian_AndDemandsCitations()
    {
        var prompt = PromptBuilder.Build("кава", [Chunk(0.6f, "про каву", 3), Chunk(0.6f, "ще кава", 4)]);

        prompt.Text.Should().Contain("ВИКЛЮЧНО");
        prompt.Text.Should().Contain("[1]");
        prompt.Text.Should().Contain("ПИТАННЯ: кава");
    }

    [Fact]
    public void Build_HonoursTheCharacterBudget()
    {
        var huge = new string('я', 3000);
        var candidates = Enumerable.Range(1, 8).Select(d => Chunk(0.6f, huge, d, [1f, d * 0.05f])).ToList();

        var prompt = PromptBuilder.Build("щось", candidates);

        prompt.Text.Length.Should().BeLessThan(PromptBuilder.MaxContextChars + 1500);
    }
}
