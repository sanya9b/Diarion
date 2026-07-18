namespace Diarion.Services;

/// <summary>Abstracts platform file-system locations so Core services stay testable.</summary>
public interface IFileSystemService
{
    string CacheDirectory { get; }
}
