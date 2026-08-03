using System;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class EmbeddingMathTests
{
    [Fact]
    public void MeanPool_AveragesOnlyTheAttendedTokens()
    {
        // Three tokens of two dimensions; the third is padding and must not pull the result down.
        var tokenVectors = new[] { 1f, 2f, 3f, 4f, 100f, 100f };
        var mask = new[] { 1, 1, 0 };

        EmbeddingMath.MeanPool(tokenVectors, mask, dimensions: 2).Should().Equal(2f, 3f);
    }

    [Fact]
    public void MeanPool_SingleToken_ReturnsItUnchanged()
    {
        EmbeddingMath.MeanPool(new[] { 5f, -7f }, new[] { 1 }, dimensions: 2).Should().Equal(5f, -7f);
    }

    [Fact]
    public void MeanPool_AllPadding_IsZeroRatherThanNaN()
    {
        // Dividing by a zero token count would poison every later comparison.
        EmbeddingMath.MeanPool(new[] { 9f, 9f }, new[] { 0 }, dimensions: 2).Should().Equal(0f, 0f);
    }

    [Fact]
    public void MeanPool_TooFewFloatsForTheMask_Throws()
    {
        var act = () => EmbeddingMath.MeanPool(new[] { 1f, 2f }, new[] { 1, 1 }, dimensions: 2);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NormalizeInPlace_ProducesUnitLength()
    {
        var vector = new[] { 3f, 4f };

        EmbeddingMath.NormalizeInPlace(vector);

        vector.Should().Equal(0.6f, 0.8f);
        EmbeddingMath.Dot(vector, vector).Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void NormalizeInPlace_ZeroVector_IsLeftAloneRatherThanNaN()
    {
        var vector = new[] { 0f, 0f, 0f };

        EmbeddingMath.NormalizeInPlace(vector);

        vector.Should().Equal(0f, 0f, 0f);
    }

    [Fact]
    public void Dot_MatchesTheNaiveSumBeyondOneSimdWidth()
    {
        // 384 is the real embedding width and is not a multiple of every SIMD register size, so
        // this also exercises the scalar tail of the vectorised loop.
        var a = new float[384];
        var b = new float[384];
        var expected = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = (i % 7) - 3;
            b[i] = (i % 5) - 2;
            expected += (double)a[i] * b[i];
        }

        EmbeddingMath.Dot(a, b).Should().BeApproximately((float)expected, 1e-2f);
    }

    [Fact]
    public void Dot_MismatchedLengths_Throws()
    {
        var act = () => EmbeddingMath.Dot(new[] { 1f, 2f }, new[] { 1f });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cosine_IdenticalDirection_IsOneRegardlessOfMagnitude()
    {
        EmbeddingMath.Cosine(new[] { 1f, 1f }, new[] { 50f, 50f }).Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void Cosine_Orthogonal_IsZero()
    {
        EmbeddingMath.Cosine(new[] { 1f, 0f }, new[] { 0f, 1f }).Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void Cosine_Opposite_IsMinusOne()
    {
        EmbeddingMath.Cosine(new[] { 1f, 2f }, new[] { -1f, -2f }).Should().BeApproximately(-1f, 1e-5f);
    }

    [Fact]
    public void Cosine_ZeroVector_IsZeroRatherThanNaN()
    {
        EmbeddingMath.Cosine(new[] { 0f, 0f }, new[] { 1f, 1f }).Should().Be(0f);
    }

    [Fact]
    public void DotNormalized_EqualsCosine_ForNormalizedInputs()
    {
        var a = new[] { 1f, 2f, 3f };
        var b = new[] { -4f, 5f, 6f };
        var expected = EmbeddingMath.Cosine(a, b);

        EmbeddingMath.NormalizeInPlace(a);
        EmbeddingMath.NormalizeInPlace(b);

        EmbeddingMath.DotNormalized(a, b).Should().BeApproximately(expected, 1e-5f);
    }

    [Fact]
    public void ToBytes_RoundTripsThroughFromBytes()
    {
        var original = new[] { 0f, 1.5f, -2.25f, float.MaxValue, float.MinValue };

        var restored = EmbeddingMath.FromBytes(EmbeddingMath.ToBytes(original));

        restored.Should().Equal(original);
    }

    [Fact]
    public void ToBytes_UsesFourBytesPerDimension()
    {
        EmbeddingMath.ToBytes(new float[384]).Should().HaveCount(1536);
    }

    [Fact]
    public void FromBytes_TruncatedBlob_Throws()
    {
        var act = () => EmbeddingMath.FromBytes(new byte[6]);

        act.Should().Throw<ArgumentException>();
    }
}
