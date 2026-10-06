using System.Linq;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Moves the single <c>LastPeriodStartDate</c> anchor into the cycle log as one real episode, so the
/// adaptive forecast starts from what the user already told us instead of asking them to begin again.
/// The episode is given the profile's period length, which is the only length information that existed.
///
/// Unlike the earlier migrations this one moves data rather than reshaping rows in place. Idempotent by
/// the emptiness of the target collection: once anything is logged, the anchor has either been migrated
/// already or been superseded by real logging, and either way must not be poured in a second time.
/// </summary>
public sealed class M005_MigrateLastPeriodDate : IMigration
{
    public int ToVersion => 5;

    public void Up(LiteDatabase db)
    {
        var logs = db.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection);
        if (logs.Count() > 0) return;

        var profiles = db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection);
        var profile = profiles.FindAll().FirstOrDefault();
        if (profile?.LastPeriodStartDate == null) return;

        var start = profile.LastPeriodStartDate.Value.Date;
        var length = profile.GetNormalizedPeriodLength();

        logs.InsertBulk(Enumerable.Range(0, length).Select(offset => new CycleLog { Date = start.AddDays(offset) }));

        // Cleared so nothing reads it again: the log is the single source of cycle history from here on.
        profile.LastPeriodStartDate = null;
        profiles.Update(profile);
    }
}
