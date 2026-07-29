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

    private ILiteCollection<UserProfile> ProfileCollection => _dbContext.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);

    public Task<UserProfile> GetUserProfileAsync()
    {
        return Task.Run(() =>
        {
            var profile = ProfileCollection.FindAll().FirstOrDefault();
            if (profile == null)
            {
                profile = new UserProfile();
                profile.NormalizeCycleSettings();
                profile.NormalizeStreakSettings();
                ProfileCollection.Insert(profile);
            }
            else
            {
                // Both must run, so no short-circuiting: a fixed cycle length must not hide a bad quota.
                var changed = profile.NormalizeCycleSettings();
                changed |= profile.NormalizeStreakSettings();

                if (changed) ProfileCollection.Update(profile);
            }

            return profile;
        });
    }

    public Task SaveUserProfileAsync(UserProfile profile)
    {
        return Task.Run(() =>
        {
            profile.NormalizeCycleSettings();
            profile.NormalizeStreakSettings();
            ProfileCollection.Upsert(profile);
        });
    }

    public Task ClearAllDataAsync()
    {
        return Task.Run(() =>
        {
            _dbContext.DropAllData();
        });
    }
}