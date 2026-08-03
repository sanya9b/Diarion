namespace Diarion.Services.Ai;

/// <summary>
/// Where the encoder's files are, and what shape they produce. Phase B's model store implements
/// this over downloaded files; until then it is the single seam between "a model exists" and the
/// runtime that loads it.
/// </summary>
public interface IEmbeddingModelLocator
{
    /// <summary>Null when no encoder is installed — not yet downloaded, or evicted.</summary>
    EmbeddingModelFiles? TryLocate();
}

/// <param name="ModelId">Stamped on every row the model produces, so a model change invalidates them.</param>
/// <param name="OnnxPath">Absolute path to the encoder graph.</param>
/// <param name="TokenizerPath">Absolute path to the raw <c>sentencepiece.bpe.model</c> protobuf.</param>
/// <param name="Dimensions">Width of the vectors the model emits.</param>
/// <param name="MaxTokens">Sequence length the graph was exported with.</param>
public sealed record EmbeddingModelFiles(
    string ModelId,
    string OnnxPath,
    string TokenizerPath,
    int Dimensions,
    int MaxTokens);
