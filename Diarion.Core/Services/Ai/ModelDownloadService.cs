using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

public class ModelDownloadService : IModelDownloadService
{
    /// <summary>Extension for a file still being fetched, so a half-download is never mistaken for a model.</summary>
    private const string PartialSuffix = ".partial";

    private const int CopyBufferBytes = 128 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IAiModelPathProvider _paths;

    public ModelDownloadService(HttpClient httpClient, IAiModelPathProvider paths)
    {
        _httpClient = httpClient;
        _paths = paths;
    }

    public ModelInstallState GetState(AiModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var directory = _paths.GetModelDirectory(model.Id);

        if (model.Files.Any(f => File.Exists(Path.Combine(directory, f.LocalName + PartialSuffix))))
        {
            return ModelInstallState.Downloading;
        }

        var present = model.Files.Select(f => new FileInfo(Path.Combine(directory, f.LocalName))).ToList();
        if (present.Any(f => !f.Exists))
        {
            return ModelInstallState.NotInstalled;
        }

        // Size is a cheap proxy for the digest, which would mean re-reading 120 MB every time the
        // settings screen renders. The real digest is checked once, at the end of the download.
        var sizes = model.Files.Select(f => f.SizeBytes).ToList();
        return present.Select(f => f.Length).SequenceEqual(sizes)
            ? ModelInstallState.Installed
            : ModelInstallState.Corrupt;
    }

    public async Task<bool> DownloadAsync(
        AiModelDescriptor model,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var directory = _paths.GetModelDirectory(model.Id);
        Directory.CreateDirectory(directory);

        var total = model.TotalSizeBytes;
        long completedBytes = 0;

        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var finalPath = Path.Combine(directory, file.LocalName);
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length == file.SizeBytes)
            {
                completedBytes += file.SizeBytes;
                progress?.Report(new ModelDownloadProgress(model.Id, completedBytes, total));
                continue;
            }

            var alreadyHave = completedBytes;
            var succeeded = await DownloadFileAsync(
                model,
                file,
                finalPath,
                bytes => progress?.Report(new ModelDownloadProgress(model.Id, alreadyHave + bytes, total)),
                cancellationToken).ConfigureAwait(false);

            if (!succeeded)
            {
                return false;
            }

            completedBytes += file.SizeBytes;
            progress?.Report(new ModelDownloadProgress(model.Id, completedBytes, total));
        }

        return true;
    }

    public void Delete(AiModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var directory = _paths.GetModelDirectory(model.Id);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<bool> DownloadFileAsync(
        AiModelDescriptor model,
        AiModelFile file,
        string finalPath,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        var partialPath = finalPath + PartialSuffix;
        var resumeFrom = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        // A partial larger than the expected size is not a partial; it is a mismatch between this
        // build's catalogue and whatever wrote the file. Start over rather than reason about it.
        if (resumeFrom >= file.SizeBytes)
        {
            File.Delete(partialPath);
            resumeFrom = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, model.BuildFileUrl(file));
        if (resumeFrom > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // A server that ignores Range answers 200 with the whole file; honouring the resume offset
        // then would splice the beginning of the file onto itself.
        if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            resumeFrom = 0;
        }
        else if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(
                         partialPath,
                         resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         CopyBufferBytes,
                         useAsync: true))
        {
            var buffer = new byte[CopyBufferBytes];
            var written = resumeFrom;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                reportBytes(written);
            }
        }

        if (!await MatchesDigestAsync(partialPath, file.Sha256, cancellationToken).ConfigureAwait(false))
        {
            // Keeping a file that failed verification would let the size check in GetState call it
            // installed on the next launch.
            File.Delete(partialPath);
            return false;
        }

        File.Move(partialPath, finalPath, overwrite: true);
        return true;
    }

    private static async Task<bool> MatchesDigestAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, useAsync: true);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(actual).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Where model files live. Implemented in the head project, which knows the app's storage.</summary>
public interface IAiModelPathProvider
{
    string GetModelDirectory(string modelId);
}
