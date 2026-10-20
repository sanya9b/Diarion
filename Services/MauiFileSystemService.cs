using System.IO;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

public class MauiFileSystemService : IFileSystemService
{
    // A debug sandbox run (DebugSandbox) redirects both; in Release the sandbox is always null.
    public string CacheDirectory => DebugSandbox.CacheDirectory ?? FileSystem.CacheDirectory;

    public string AppDataDirectory => DebugSandbox.DataDirectory ?? FileSystem.AppDataDirectory;
}
