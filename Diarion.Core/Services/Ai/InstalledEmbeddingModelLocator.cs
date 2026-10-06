using System.IO;
using System.Linq;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>
/// Points the runtime at whichever embedding model is actually installed.
/// </summary>
/// <remarks>
/// Asks the download service rather than the filesystem, so "installed" stays defined in exactly
/// one place — including the case where files are present but the wrong size, which is a corrupt
/// install and must not be loaded.
/// </remarks>
public class InstalledEmbeddingModelLocator : IEmbeddingModelLocator
{
    private const string OnnxFileName = "model.onnx";
    private const string TokenizerFileName = "sentencepiece.bpe.model";

    private readonly IModelDownloadService _downloads;
    private readonly IAiModelPathProvider _paths;

    public InstalledEmbeddingModelLocator(IModelDownloadService downloads, IAiModelPathProvider paths)
    {
        _downloads = downloads;
        _paths = paths;
    }

    public EmbeddingModelFiles? TryLocate()
    {
        var model = AiModelCatalog.OfKind(AiModelKind.Embedding)
            .FirstOrDefault(m => _downloads.GetState(m) == ModelInstallState.Installed);

        if (model is null)
        {
            return null;
        }

        var directory = _paths.GetModelDirectory(model.Id);

        return new EmbeddingModelFiles(
            model.Id,
            Path.Combine(directory, OnnxFileName),
            Path.Combine(directory, TokenizerFileName),
            model.Dimensions,
            model.MaxTokens);
    }
}
