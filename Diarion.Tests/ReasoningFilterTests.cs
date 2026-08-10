using System.Linq;
using System.Text;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Qwen3 narrates its way to an answer unless asked not to, and asking is not a guarantee. These
/// pin the guarantee: reasoning reaches neither the screen nor the citation parser.
/// </summary>
public class ReasoningFilterTests
{
    /// <summary>Runs text through the streaming path one character at a time — the worst case for
    /// tags split across pieces, and the closest thing to how tokens actually arrive.</summary>
    private static string StreamByChar(string text)
    {
        var filter = new ReasoningFilter();
        var seen = new StringBuilder();

        foreach (var c in text)
        {
            seen.Append(filter.Push(c.ToString()));
        }

        seen.Append(filter.Flush());
        return seen.ToString();
    }

    [Fact]
    public void AnAnswerWithNoReasoning_PassesThroughUntouched()
    {
        const string answer = "Ви гуляли містом з Олегом [2].";

        StreamByChar(answer).Should().Be(answer);
        ReasoningFilter.Strip(answer).Should().Be(answer);
    }

    [Fact]
    public void TheMonologueIsDropped_TheAnswerIsKept()
    {
        // Shortened from what the running app actually showed: English deliberation quoting markers
        // the model was only weighing, then the Ukrainian sentence it settled on.
        const string produced =
            "<think>\nOkay, the user asks about work. Record [1] and [7] mention it.\n</think>\n\n" +
            "Ви писали про стрес на роботі [1].";

        StreamByChar(produced).Trim().Should().Be("Ви писали про стрес на роботі [1].");
        ReasoningFilter.Strip(produced).Should().Be("Ви писали про стрес на роботі [1].");
    }

    [Fact]
    public void MarkersInsideTheMonologue_NeverReachTheCitationParser()
    {
        // The bug as seen: four chips, none of them from a sentence the model committed to.
        var offered = Enumerable.Range(1, 8)
            .Select(i => new ChatCitation(i, "diary", $"id{i}", new System.DateTime(2026, 7, 5), $"текст {i}"))
            .ToList();

        const string produced =
            "<think>Records [1] to [6] say one thing, [7] and [8] another.</think>Ви писали про роботу [3].";

        var parsed = CitationParser.Parse(ReasoningFilter.Strip(produced), offered);

        parsed.IsRefusal.Should().BeFalse();
        parsed.Used.Select(c => c.Marker).Should().Equal(3);
    }

    [Fact]
    public void ReasoningCutOffByTheTokenBudget_LeavesNothing()
    {
        // Exactly the failure in the app: the budget ran out mid-thought. Nothing survives, the
        // parser sees an empty answer, and the user gets a refusal instead of half a monologue.
        const string produced = "<think>Okay, let's see. The user is asking about work. Wait, the user's question is \"Що я";

        StreamByChar(produced).Should().BeEmpty();
        ReasoningFilter.Strip(produced).Should().BeEmpty();
        CitationParser.Parse(ReasoningFilter.Strip(produced), []).IsRefusal.Should().BeTrue();
    }

    [Fact]
    public void ATagSplitAcrossPieces_IsStillRecognised()
    {
        // The streaming case that a naive contains-check gets wrong: "<th" arrives, then "ink>".
        var filter = new ReasoningFilter();
        var seen = new StringBuilder();

        foreach (var piece in new[] { "<th", "ink>hid", "den</th", "ink>", "видно" })
        {
            seen.Append(filter.Push(piece));
        }

        seen.Append(filter.Flush());
        seen.ToString().Should().Be("видно");
    }

    [Fact]
    public void NothingIsReleasedWhileATagCouldStillBeForming()
    {
        // The hold-back has to be temporary: text that merely starts like a tag must come out.
        var filter = new ReasoningFilter();

        filter.Push("Ви писали <").Should().Be("Ви писали ");
        filter.Push("3 листи.").Should().Be("<3 листи.");
        filter.Flush().Should().BeEmpty();
    }

    [Fact]
    public void SeveralBlocks_AreAllRemoved()
    {
        const string produced = "<think>a</think>Перше речення. <think>b</think>Друге [1].";

        StreamByChar(produced).Should().Be("Перше речення. Друге [1].");
        ReasoningFilter.Strip(produced).Should().Be("Перше речення. Друге [1].");
    }

    [Fact]
    public void ACloseTagWithNoOpen_TakesEverythingBeforeItAsReasoning()
    {
        // What a template that pre-fills the open tag produces. Strip can resolve it because it sees
        // the whole answer; the stream can only drop the tag, which is why the citations are read
        // from Strip and not from what was shown.
        const string produced = "Okay, so record [4] is the one.</think>Ви ходили в кіно [4].";

        ReasoningFilter.Strip(produced).Should().Be("Ви ходили в кіно [4].");
    }

    [Fact]
    public void EmptyAndNullAreNotSpecialCases()
    {
        var filter = new ReasoningFilter();

        filter.Push(null).Should().BeEmpty();
        filter.Push(string.Empty).Should().BeEmpty();
        filter.Flush().Should().BeEmpty();

        ReasoningFilter.Strip(null).Should().BeEmpty();
        ReasoningFilter.Strip("   ").Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyThinkBlock_IsTheSwitchWorking()
    {
        // What /no_think actually produces: the model opens and closes the block immediately.
        const string produced = "<think>\n\n</think>\n\nВи писали про роботу [1].";

        StreamByChar(produced).Trim().Should().Be("Ви писали про роботу [1].");
    }
}
