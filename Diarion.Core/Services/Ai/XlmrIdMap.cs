namespace Diarion.Services.Ai;

/// <summary>
/// Translates raw SentencePiece ids into the vocabulary XLM-RoBERTa models were trained on.
///
/// <para>
/// <see cref="Microsoft.ML.Tokenizers.SentencePieceTokenizer"/> returns ids straight out of the
/// <c>sentencepiece.bpe.model</c> protobuf, but every XLM-R checkpoint on HuggingFace was trained
/// against fairseq's renumbered vocabulary. Feeding raw ids to the model produces embeddings that
/// look entirely normal — finite, correctly shaped, plausibly distributed — and mean nothing.
/// Nothing downstream can detect it, which is why this lives behind golden fixtures.
/// </para>
/// </summary>
public static class XlmrIdMap
{
    public const int BosId = 0;
    public const int PadId = 1;
    public const int EosId = 2;
    public const int UnkId = 3;
    public const int MaskId = 250001;

    /// <summary>Size of the fairseq vocabulary, including <c>&lt;mask&gt;</c> at the very end.</summary>
    public const int VocabSize = 250002;

    private const int SentencePieceUnkId = 0;
    private const int SentencePieceBosId = 1;
    private const int SentencePieceEosId = 2;

    /// <summary>
    /// Maps one SentencePiece id to its fairseq equivalent. The four control tokens are reordered
    /// outright; every other piece shifts up by one, because fairseq inserts <c>&lt;pad&gt;</c>
    /// into a slot SentencePiece does not have.
    /// </summary>
    public static int ToFairseq(int sentencePieceId) => sentencePieceId switch
    {
        SentencePieceUnkId => UnkId,
        SentencePieceBosId => BosId,
        SentencePieceEosId => EosId,
        _ => sentencePieceId + 1,
    };

    /// <summary>Inverse of <see cref="ToFairseq"/>, for decoding ids back to text.</summary>
    public static int ToSentencePiece(int fairseqId) => fairseqId switch
    {
        UnkId => SentencePieceUnkId,
        BosId => SentencePieceBosId,
        EosId => SentencePieceEosId,
        PadId => SentencePieceUnkId,
        _ => fairseqId - 1,
    };

    /// <summary>
    /// Wraps mapped piece ids in the sentence markers the encoder expects and pads to
    /// <paramref name="maxLength"/>. Sequences longer than the limit are truncated so that the
    /// closing marker survives — a sequence without <c>&lt;/s&gt;</c> degrades pooling quality.
    /// </summary>
    public static (int[] InputIds, int[] AttentionMask) BuildInput(
        IReadOnlyList<int> sentencePieceIds,
        int maxLength)
    {
        ArgumentNullException.ThrowIfNull(sentencePieceIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);

        var contentLength = Math.Min(sentencePieceIds.Count, maxLength - 2);
        var tokenCount = contentLength + 2;

        var inputIds = new int[maxLength];
        var attentionMask = new int[maxLength];

        inputIds[0] = BosId;
        for (var i = 0; i < contentLength; i++)
        {
            inputIds[i + 1] = ToFairseq(sentencePieceIds[i]);
        }
        inputIds[contentLength + 1] = EosId;

        for (var i = 0; i < tokenCount; i++)
        {
            attentionMask[i] = 1;
        }

        for (var i = tokenCount; i < maxLength; i++)
        {
            inputIds[i] = PadId;
        }

        return (inputIds, attentionMask);
    }
}
