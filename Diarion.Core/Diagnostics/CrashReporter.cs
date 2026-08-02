using System;
using System.IO;
using Diarion.Services;

namespace Diarion.Diagnostics;

/// <inheritdoc />
public sealed class CrashReporter : ICrashReporter
{
    private const string FileName = "last-crash.log";

    private readonly IFileSystemService _fileSystem;
    private readonly string _appVersion;

    public CrashReporter(IFileSystemService fileSystem, string appVersion)
    {
        _fileSystem = fileSystem;
        _appVersion = appVersion;
    }

    private string Path => System.IO.Path.Combine(_fileSystem.AppDataDirectory, FileName);

    public bool HasReport
    {
        get
        {
            try { return File.Exists(Path); }
            catch { return false; }
        }
    }

    public string? ReportPath => HasReport ? Path : null;

    public void Record(string source, Exception? exception)
    {
        // Everything here runs while the process is on its way down. A throw would replace a
        // diagnosable crash with an undiagnosable one, so nothing is allowed to escape.
        try
        {
            var report = CrashReport.Format(source, exception, DateTime.UtcNow, _appVersion);
            Directory.CreateDirectory(_fileSystem.AppDataDirectory);
            File.WriteAllText(Path, report);
        }
        catch
        {
            // Nothing sensible is left to do.
        }
    }

    public string? ReadLast()
    {
        try
        {
            return File.Exists(Path) ? File.ReadAllText(Path) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // A leftover report is harmless; it just gets offered again.
        }
    }
}
