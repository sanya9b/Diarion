using System.Threading.Tasks;

namespace Diarion.Services;

public interface IBackupService
{
    Task<bool> ExportBackupAsync();
    Task<bool> ImportBackupAsync();
}