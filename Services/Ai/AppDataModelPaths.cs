using System.IO;
using Diarion.Services.Ai;
using Microsoft.Maui.Storage;

namespace Diarion.Services.Ai;

/// <summary>
/// Where downloaded models live on this device.
/// </summary>
/// <remarks>
/// Under <see cref="FileSystem.AppDataDirectory"/> and deliberately not under
/// <c>CacheDirectory</c>: Android empties the cache under storage pressure, and a model measured in
/// hundreds of megabytes is exactly what it would empty first. The manifest already sets
/// <c>allowBackup=false</c>, so nothing here reaches Google's cloud either.
/// </remarks>
public sealed class AppDataModelPaths : IAiModelPathProvider
{
    public const string ModelsFolder = "ai-models";

    public string GetModelDirectory(string modelId) =>
        Path.Combine(FileSystem.AppDataDirectory, ModelsFolder, modelId);
}
