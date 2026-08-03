using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public enum ModelInstallState
{
    NotInstalled,
    Downloading,
    Installed,

    /// <summary>Files are present but a digest did not match, so they are not trusted.</summary>
    Corrupt,
}

/// <param name="BytesReceived">Across all of the model's files, including bytes resumed from disk.</param>
public readonly record struct ModelDownloadProgress(string ModelId, long BytesReceived, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0d : Math.Clamp((double)BytesReceived / TotalBytes, 0d, 1d);
}

/// <summary>
/// Fetches model files from HuggingFace into the app's private storage.
/// </summary>
/// <remarks>
/// This is the only component in the app that opens a socket, and it only ever performs GETs
/// against pinned commit URLs. Nothing about the user is sent — no identifiers, no query strings,
/// no telemetry. That constraint is the entire justification for the INTERNET permission.
/// </remarks>
public interface IModelDownloadService
{
    ModelInstallState GetState(AiModelDescriptor model);

    /// <summary>
    /// Downloads whatever is missing, resuming partial files, and verifies every digest.
    /// Returns false if any file failed to arrive or failed verification.
    /// </summary>
    Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the model's files, partial ones included.</summary>
    void Delete(AiModelDescriptor model);
}
