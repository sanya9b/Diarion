using System.Linq;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The fairseq remap is the one place in the AI module where a mistake is completely silent: wrong
/// ids still produce finite, correctly shaped, plausibly distributed vectors. Nothing downstream
/// throws, nothing logs, search just quietly gets worse. Hence the pedantry here.
/// </summary>
public class XlmrIdMapTests
{
    [Theory]
    [InlineData(0, XlmrIdMap.UnkId)]   // SentencePiece <unk> is 0, fairseq puts it at 3
    [InlineData(1, XlmrIdMap.BosId)]   // <s>  1 -> 0
    [InlineData(2, XlmrIdMap.EosId)]   // </s> 2 -> 2 via the special case, not the shift
    public void ToFairseq_ControlTokens_AreReordered(int sentencePieceId, int expected)
    {
        XlmrIdMap.ToFairseq(sentencePieceId).Should().Be(expected);
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(100, 101)]
    [InlineData(250000, 250001)]
    public void ToFairseq_OrdinaryPieces_ShiftUpByOne(int sentencePieceId, int expected)
    {
        XlmrIdMap.ToFairseq(sentencePieceId).Should().Be(expected);
    }

    [Fact]
    public void ToFairseq_NeverCollidesAcrossTheControlBoundary()
    {
        // The +1 shift exists to leave room for <pad>; if any ordinary piece landed on a control id
        // the vocabulary would be ambiguous.
        var mapped = Enumerable.Range(0, 5000).Select(XlmrIdMap.ToFairseq).ToArray();

        mapped.Should().OnlyHaveUniqueItems();
        mapped.Should().NotContain(XlmrIdMap.PadId);
    }

    [Theory]
    [InlineData(XlmrIdMap.UnkId, 0)]
    [InlineData(XlmrIdMap.BosId, 1)]
    [InlineData(XlmrIdMap.EosId, 2)]
    [InlineData(4, 3)]
    [InlineData(101, 100)]
    public void ToSentencePiece_InvertsToFairseq(int fairseqId, int expected)
    {
        XlmrIdMap.ToSentencePiece(fairseqId).Should().Be(expected);
    }

    [Fact]
    public void BuildInput_WrapsInSentenceMarkersAndPads()
    {
        var (ids, mask) = XlmrIdMap.BuildInput(new[] { 10, 11, 12 }, maxLength: 8);

        ids.Should().Equal(
            XlmrIdMap.BosId, 11, 12, 13, XlmrIdMap.EosId,
            XlmrIdMap.PadId, XlmrIdMap.PadId, XlmrIdMap.PadId);
        mask.Should().Equal(1, 1, 1, 1, 1, 0, 0, 0);
    }

    [Fact]
    public void BuildInput_ExactFit_LeavesNoPadding()
    {
        var (ids, mask) = XlmrIdMap.BuildInput(new[] { 10, 11 }, maxLength: 4);

        ids.Should().Equal(XlmrIdMap.BosId, 11, 12, XlmrIdMap.EosId);
        mask.Should().AllSatisfy(m => m.Should().Be(1));
    }

    [Fact]
    public void BuildInput_OverlongInput_TruncatesButKeepsTheClosingMarker()
    {
        // Dropping </s> would degrade pooling on exactly the longest entries, which are the ones
        // most worth finding.
        var (ids, mask) = XlmrIdMap.BuildInput(new[] { 10, 20, 30, 40, 50 }, maxLength: 4);

        ids.Should().HaveCount(4);
        ids[0].Should().Be(XlmrIdMap.BosId);
        ids[^1].Should().Be(XlmrIdMap.EosId);
        mask.Should().AllSatisfy(m => m.Should().Be(1));
    }

    [Fact]
    public void BuildInput_EmptyInput_IsJustTheMarkers()
    {
        var (ids, mask) = XlmrIdMap.BuildInput(System.Array.Empty<int>(), maxLength: 4);

        ids.Should().Equal(XlmrIdMap.BosId, XlmrIdMap.EosId, XlmrIdMap.PadId, XlmrIdMap.PadId);
        mask.Should().Equal(1, 1, 0, 0);
    }

    [Fact]
    public void MaskIdSitsAtTheTopOfTheVocabulary()
    {
        XlmrIdMap.MaskId.Should().Be(XlmrIdMap.VocabSize - 1);
    }
}
