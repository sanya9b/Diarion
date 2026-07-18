using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>Abstracts the platform share sheet.</summary>
public interface IShareService
{
    Task ShareFileAsync(string title, string filePath);
}
