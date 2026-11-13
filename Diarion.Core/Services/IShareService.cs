using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>Abstracts the platform share sheet.</summary>
public interface IShareService
{
    Task ShareFileAsync(string title, string filePath);

    /// <summary>A link with a short text, through the same sheet.</summary>
    Task ShareLinkAsync(string title, string text, string url);
}
