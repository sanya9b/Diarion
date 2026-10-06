using System.Threading.Tasks;

namespace Diarion.Services;

public enum ExportFormat
{
    Json,
    Csv,
    Markdown
}

/// <summary>
/// Exports the user's data in open, human-readable formats (separate from the binary backup).
/// Everything is produced on-device; nothing is uploaded.
/// </summary>
public interface IExportService
{
    /// <summary>Builds the export content as a string (pure; used by tests and the share flow).</summary>
    Task<string> BuildAsync(ExportFormat format);

    /// <summary>Builds the export, writes it to a temp file, and opens the share sheet.</summary>
    Task<bool> ExportAndShareAsync(ExportFormat format);
}
