using System.IO;
using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>Abstracts the platform file picker for choosing a backup file to import.</summary>
public interface IFilePickerService
{
    /// <summary>Prompts the user to pick a backup file; returns a readable stream, or null if cancelled.</summary>
    Task<Stream?> PickBackupFileAsync(string title);
}
