using System.IO;
using System.Linq;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>
/// Points the generator at whichever generative model is installed.
/// </summary>
/// <remarks>
/// Mirrors <see cref="InstalledEmbeddingModelLocator"/>, and for the same reason: "installed" is
/// defined once, by the download service, including the case where files are present but the wrong
/// size. A directory is enough here because ORT-GenAI loads a folder, not a file.
/// </remarks>
public class InstalledGenerativeModelLocator : IGenerativeModelLocator
{
    private readonly IModelDownloadService _downloads;
    private readonly IAiModelPathProvider _paths;

    public InstalledGenerativeModelLocator(IModelDownloadService downloads, IAiModelPathProvider paths)
    {
        _downloads = downloads;
        _paths = paths;
    }

    public GenerativeModelFiles? TryLocate()
    {
        var model = AiModelCatalog.OfKind(AiModelKind.Generation)
            .FirstOrDefault(m => _downloads.GetState(m) == ModelInstallState.Installed);

        if (model is null)
        {
            return null;
        }

        var directory = _paths.GetModelDirectory(model.Id);
        return Directory.Exists(directory) ? new GenerativeModelFiles(model.Id, directory) : null;
    }
}
