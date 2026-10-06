using System.Numerics;
using System.Runtime.InteropServices;

namespace Diarion.Services.Ai;

/// <summary>
/// Vector arithmetic shared by the embedder, the store and the ranker. Kept in Core, away from the
/// native ONNX handle, so the parts that quietly decide search quality are unit-testable.
/// </summary>
/// <remarks>
/// Uses <see cref="Vector{T}"/> rather than <c>TensorPrimitives</c> to stay inside the shared
/// framework — the AI module already carries enough native dependencies.
/// </remarks>
public static class EmbeddingMath
{
    /// <summary>
    /// Averages token vectors over the real tokens only. Padding must be excluded: including it
    /// drags every short text toward the same point and collapses ranking on exactly the entries a
    /// diary has most of.
    /// </summary>
    /// <param name="tokenVectors">Flattened [tokenCount x dimensions] row-major model output.</param>
    public static float[] MeanPool(ReadOnlySpan<float> tokenVectors, ReadOnlySpan<int> attentionMask, int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);

        var tokenCount = attentionMask.Length;
        if (tokenVectors.Length < tokenCount * dimensions)
        {
            throw new ArgumentException(
                $"Expected at least {tokenCount * dimensions} floats for {tokenCount} tokens of {dimensions} dimensions, got {tokenVectors.Length}.",
                nameof(tokenVectors));
        }

        var pooled = new float[dimensions];
        var counted = 0;

        for (var token = 0; token < tokenCount; token++)
        {
            if (attentionMask[token] == 0)
            {
                continue;
            }

            var row = tokenVectors.Slice(token * dimensions, dimensions);
            for (var d = 0; d < dimensions; d++)
            {
                pooled[d] += row[d];
            }
            counted++;
        }

        if (counted > 1)
        {
            var scale = 1f / counted;
            for (var d = 0; d < dimensions; d++)
            {
                pooled[d] *= scale;
            }
        }

        return pooled;
    }

    /// <summary>
    /// Scales a vector to unit length in place. Every vector is normalized on write so that cosine
    /// similarity reduces to a dot product at query time.
    /// </summary>
    public static void NormalizeInPlace(Span<float> vector)
    {
        var norm = MathF.Sqrt(Dot(vector, vector));

        // An all-zero vector has no direction to preserve; leaving it alone keeps it at similarity
        // zero against everything rather than producing NaN that poisons the whole ranking.
        if (norm <= float.Epsilon)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }

    /// <summary>
    /// Cosine similarity for vectors that are already normalized — i.e. a plain dot product.
    /// Use <see cref="Cosine"/> when that guarantee does not hold.
    /// </summary>
    public static float DotNormalized(ReadOnlySpan<float> a, ReadOnlySpan<float> b) => Dot(a, b);

    /// <summary>Cosine similarity that does not assume normalized inputs.</summary>
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var denominator = MathF.Sqrt(Dot(a, a)) * MathF.Sqrt(Dot(b, b));
        return denominator <= float.Epsilon ? 0f : Dot(a, b) / denominator;
    }

    /// <summary>
    /// SIMD dot product. This runs once per stored chunk on every query, so it is the one piece of
    /// the search path worth hand-vectorising.
    /// </summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Vectors differ in length: {a.Length} vs {b.Length}.", nameof(b));
        }

        var width = Vector<float>.Count;
        var accumulator = Vector<float>.Zero;
        var i = 0;

        for (; i <= a.Length - width; i += width)
        {
            accumulator += new Vector<float>(a.Slice(i, width)) * new Vector<float>(b.Slice(i, width));
        }

        var sum = Vector.Dot(accumulator, Vector<float>.One);

        for (; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary>Packs a vector as little-endian float32 for storage as a LiteDB blob.</summary>
    public static byte[] ToBytes(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    /// <summary>Unpacks a blob written by <see cref="ToBytes"/>.</summary>
    public static float[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new ArgumentException($"Blob length {bytes.Length} is not a whole number of float32 values.", nameof(bytes));
        }

        var vector = new float[bytes.Length / sizeof(float)];
        bytes.CopyTo(MemoryMarshal.AsBytes(vector.AsSpan()));
        return vector;
    }
}
