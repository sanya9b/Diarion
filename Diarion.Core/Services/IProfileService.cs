using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IProfileService
{
    Task<UserProfile> GetUserProfileAsync();
    Task SaveUserProfileAsync(UserProfile profile);
    Task ClearAllDataAsync();
}