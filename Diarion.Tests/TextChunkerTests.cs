using System;
using System.Linq;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class TextChunkerTests
{
    private static string Words(int count, string prefix = "w") =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"{prefix}{i}"));

    [Fact]
    public void ChunkText_ShorterThanTarget_IsASingleChunk()
    {
        var chunks = TextChunker.ChunkText(Words(10), targetWords: 200, overlapWords: 40);

        chunks.Should().ContainSingle().Which.Should().Be(Words(10));
    }

    [Fact]
    public void ChunkText_NormalizesWhitespace()
    {
        TextChunker.ChunkText("  спав   погано\n\nбо  кава  ")
            .Should().ContainSingle().Which.Should().Be("спав погано бо кава");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void ChunkText_Blank_ProducesNothing(string? text)
    {
        TextChunker.ChunkText(text).Should().BeEmpty();
    }

    [Fact]
    public void Chunk_SkipsBlankSegments_SoCallersNeedNotFilter()
    {
        var chunks = TextChunker.Chunk(new[] { "перший", null, "   ", "другий" });

        chunks.Should().Equal("перший", "другий");
    }

    [Fact]
    public void Chunk_NeverMergesTwoSegmentsIntoOneChunk()
    {
        // A window spanning two diary fields would embed a sentence pair the user never wrote.
        var chunks = TextChunker.Chunk(new[] { "сон був поганий", "вдячний за каву" }, targetWords: 200, overlapWords: 40);

        chunks.Should().HaveCount(2);
        chunks.Should().NotContain(c => c.Contains("поганий") && c.Contains("вдячний"));
    }

    [Fact]
    public void ChunkText_LongText_OverlapsByTheRequestedAmount()
    {
        var chunks = TextChunker.ChunkText(Words(250), targetWords: 100, overlapWords: 20);

        // stride 80: [0..99], [80..179], [160..249]
        chunks.Should().HaveCount(3);
        chunks[0].Split(' ').Should().HaveCount(100);
        chunks[0].Split(' ').Last().Should().Be("w99");
        chunks[1].Split(' ').First().Should().Be("w80");
        chunks[2].Split(' ').Last().Should().Be("w249");
    }

    [Fact]
    public void ChunkText_EveryWordSurvivesSomewhere()
    {
        var chunks = TextChunker.ChunkText(Words(437), targetWords: 100, overlapWords: 20);

        var covered = chunks.SelectMany(c => c.Split(' ')).Distinct();
        covered.Should().HaveCount(437);
    }

    [Fact]
    public void ChunkText_DoesNotEmitATrailingChunkOfPureOverlap()
    {
        // 180 words at stride 80 would naively start a window at 160 that is entirely contained in
        // its predecessor's tail; the last window should be the one that reaches the end.
        var chunks = TextChunker.ChunkText(Words(180), targetWords: 100, overlapWords: 20);

        chunks.Should().HaveCount(2);
        chunks[^1].Split(' ').Last().Should().Be("w179");
    }

    [Fact]
    public void ChunkText_ExactlyTarget_IsOneChunk()
    {
        TextChunker.ChunkText(Words(100), targetWords: 100, overlapWords: 20).Should().ContainSingle();
    }

    [Fact]
    public void Chunk_OverlapNotSmallerThanTarget_Throws()
    {
        // Otherwise the window never advances and chunking cannot terminate.
        var act = () => TextChunker.Chunk(new[] { "текст" }, targetWords: 50, overlapWords: 50);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Chunk_ZeroOverlap_TilesWithoutRepeating()
    {
        var chunks = TextChunker.ChunkText(Words(30), targetWords: 10, overlapWords: 0);

        chunks.Should().HaveCount(3);
        chunks.SelectMany(c => c.Split(' ')).Should().OnlyHaveUniqueItems();
    }
}
