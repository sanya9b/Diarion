namespace Diarion.Services;

/// <summary>Abstracts platform file-system locations so Core services stay testable.</summary>
public interface IFileSystemService
{
    string CacheDirectory { get; }

    /// <summary>
    /// Persistent per-app storage. Distinct from <see cref="CacheDirectory"/> because iOS may purge
    /// the cache under storage pressure, and a crash report is worthless if it does not survive until
    /// the next launch — which is the only moment anyone is there to read it.
    /// </summary>
    string AppDataDirectory { get; }
}
