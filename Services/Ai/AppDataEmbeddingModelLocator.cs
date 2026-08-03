using System.IO;
using Diarion.Services.Ai;
using Microsoft.Maui.Storage;

namespace Diarion.Services.Ai;

/// <summary>
/// Finds the encoder among the downloaded model files.
/// </summary>
/// <remarks>
/// Files live under <see cref="FileSystem.AppDataDirectory"/> and deliberately not under
/// <c>CacheDirectory</c>: Android empties the cache under storage pressure, and a model measured in
/// hundreds of megabytes is exactly what it would empty first. The app manifest already sets
/// <c>allowBackup=false</c>, so nothing here reaches Google's cloud either.
/// </remarks>
public sealed class AppDataEmbeddingModelLocator : IEmbeddingModelLocator
{
    /// <summary>Directory the downloader writes into, relative to the app's private data.</summary>
    public const string ModelsFolder = "ai-models";

    // Phase B replaces this constant with a catalogue entry. Until a download UI exists there is
    // exactly one encoder, and pretending otherwise would be scaffolding for its own sake.
    public const string EncoderModelId = "paraphrase-multilingual-MiniLM-L12-v2-int8";

    private const string OnnxFileName = "model.onnx";
    private const string TokenizerFileName = "sentencepiece.bpe.model";
    private const int EncoderDimensions = 384;
    private const int EncoderMaxTokens = 512;

    public static string ModelDirectory(string modelId) =>
        Path.Combine(FileSystem.AppDataDirectory, ModelsFolder, modelId);

    public EmbeddingModelFiles? TryLocate()
    {
        var directory = ModelDirectory(EncoderModelId);
        var onnxPath = Path.Combine(directory, OnnxFileName);
        var tokenizerPath = Path.Combine(directory, TokenizerFileName);

        if (!File.Exists(onnxPath) || !File.Exists(tokenizerPath))
        {
            return null;
        }

        return new EmbeddingModelFiles(
            EncoderModelId,
            onnxPath,
            tokenizerPath,
            EncoderDimensions,
            EncoderMaxTokens);
    }
}
