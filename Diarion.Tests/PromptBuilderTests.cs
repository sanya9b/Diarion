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
    public void Build_ASingleMiddlingMatch_IsStillUnanswerable()
    {
        // One passage barely above the floor is a coincidence often enough to be worth refusing.
        PromptBuilder.Build("тривога", [Chunk(0.35f), Chunk(0.1f)]).IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public void Build_AMiddlingMatchPlusANearNoiseOne_IsStillUnanswerable()
    {
        // «Яку машину я купив?» — 0.445 alongside 0.311, and the answer came back as the bicycle.
        // Both passages clear MinRelevance, so the old rule called that two corroborating sources.
        PromptBuilder.Build("яку машину я купив", [Chunk(0.445f, "велосипед", 3), Chunk(0.311f, "інше", 4)])
            .IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public void Build_TwoSubstantialMatches_AreEnoughWithoutEitherStandingAlone()
    {
        // Neither reaches MinStandaloneRelevance; together they corroborate.
        PromptBuilder.Build("кава", [Chunk(0.42f, "про каву", 3), Chunk(0.36f, "знову кава", 4)])
            .IsAnswerable.Should().BeTrue();
    }

    [Fact]
    public void Build_ASingleVeryStrongMatch_AnswersOnItsOwn()
    {
        // The diary keeps most specific facts in exactly one entry. «Скільки коштував велосипед?»
        // matched its entry at 0.646 and was refused for want of a second passage; that is the
        // question retrieval handles best, not worst.
        var prompt = PromptBuilder.Build("скільки коштував велосипед", [Chunk(0.646f), Chunk(0.1f)]);

        prompt.IsAnswerable.Should().BeTrue();
        prompt.Citations.Should().ContainSingle();
    }

    [Fact]
    public void Build_ASingleMatchExactlyAtTheStandaloneBar_Answers()
    {
        PromptBuilder.Build("щось", [Chunk(PromptBuilder.MinStandaloneRelevance)])
            .IsAnswerable.Should().BeTrue();
    }

    [Fact]
    public void Build_TheStandaloneBarAdmitsOnlyThePassagesThatEarnedIt()
    {
        // Opening the gate does not lower the floor: the weak chunk stays out of the prompt.
        var prompt = PromptBuilder.Build("велосипед", [Chunk(0.6f, "про велосипед", 3), Chunk(0.2f, "щось інше", 4)]);

        prompt.Citations.Should().ContainSingle();
        prompt.Text.Should().NotContain("щось інше");
    }

    [Fact]
    public void Build_TwoGoodMatches_IsAnswerable()
    {
        var prompt = PromptBuilder.Build("кава", [Chunk(0.6f, "про каву", 3), Chunk(0.5f, "знову кава", 4)]);

        prompt.IsAnswerable.Should().BeTrue();
        prompt.Citations.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ExactlyAtTheCorroborationBar_Counts()
    {
        PromptBuilder.Build("щось", [
            Chunk(PromptBuilder.SubstantialRelevance),
            Chunk(PromptBuilder.SubstantialRelevance),
        ]).IsAnswerable.Should().BeTrue();
    }

    [Fact]
    public void Build_ExactlyAtTheFloor_ReachesThePromptOnceTheGateIsOpen()
    {
        // The floor decides what is worth showing, not what opens the gate. A passage at 0.28 is
        // context for an answer two other passages already corroborated.
        var prompt = PromptBuilder.Build("кава", [
            Chunk(0.6f, "про каву", 3),
            Chunk(0.5f, "знову кава", 4),
            Chunk(PromptBuilder.MinRelevance, "дотичне", 5),
        ]);

        prompt.Citations.Should().HaveCount(3);
        prompt.Text.Should().Contain("дотичне");
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
    public void Build_CarriesAWorkedExample()
    {
        // Measured, not stylistic: without it Qwen3-1.7B echoed all four passages back and scored
        // 1/4 on the Ukrainian evaluation; with it, 4/4. The block format invites enumeration and
        // only an example shows the model what an answer looks like.
        var prompt = PromptBuilder.Build("кава", [Chunk(0.6f, "про каву", 3), Chunk(0.6f, "ще кава", 4)]);

        prompt.Text.Should().Contain("Приклад.");
        prompt.Text.Should().Contain("Ви гуляли містом з Олегом [2].");
        prompt.Text.Should().Contain("Не перелічуй записи");
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
