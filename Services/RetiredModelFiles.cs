using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

/// <summary>
/// Deletes what the retired on-device AI left on the device: the downloaded models under
/// <c>AppDataDirectory/ai-models</c> (the encoder alone was over a hundred megabytes, the generative model
/// over a gigabyte) with any half-finished <c>.partial</c> downloads among them. The AI was removed on
/// 2026-10-21 and nothing reads these files any more. Runs off the UI thread at every start and costs one
/// directory check once the folder is gone; a failure is left for the next start rather than reported,
/// because there is nothing the user could do about it.
/// </summary>
internal static class RetiredModelFiles
{
    private const string FolderName = "ai-models";

    public static void Sweep() => _ = Task.Run(() =>
    {
        try
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, FolderName);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Retired model files not removed yet: {ex.Message}");
        }
    });
}
