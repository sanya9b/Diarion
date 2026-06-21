using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class ProfileService : IProfileService
{
    private readonly IDatabaseContext _dbContext;

    public ProfileService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<UserProfile> ProfileCollection => _dbContext.GetCollection<UserProfile>("profile");

    public Task<UserProfile> GetUserProfileAsync()
    {
        return Task.Run(() =>
        {
            var profile = ProfileCollection.FindAll().FirstOrDefault();
            if (profile == null)
            {
                profile = new UserProfile();
                profile.NormalizeCycleSettings();
                ProfileCollection.Insert(profile);
            }
            else if (profile.NormalizeCycleSettings())
            {
                ProfileCollection.Update(profile);
            }

            return profile;
        });
    }

    public Task SaveUserProfileAsync(UserProfile profile)
    {
        return Task.Run(() =>
        {
            profile.NormalizeCycleSettings();
            ProfileCollection.Upsert(profile);
        });
    }
}