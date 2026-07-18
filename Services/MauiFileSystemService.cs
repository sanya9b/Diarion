using Diarion.Services;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

public class MauiFileSystemService : IFileSystemService
{
    public string CacheDirectory => FileSystem.CacheDirectory;
}
