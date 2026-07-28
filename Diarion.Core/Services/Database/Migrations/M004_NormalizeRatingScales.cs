using System;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Sleep quality and health status were once rated out of ten and are now rated out of five, so old
/// entries read as impossibly high — a "9" renders as five filled stars with nothing to show for the
/// rest. Values above five are halved onto the new scale; a value of five or less is left alone
/// because there is no way to tell an old 4-of-10 from a new 4-of-5, and guessing would corrupt data
/// that is already correct.
/// </summary>
public sealed class M004_NormalizeRatingScales : IMigration
{
    public int ToVersion => 4;

    public void Up(LiteDatabase db)
    {
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

        foreach (var entry in entries.FindAll())
        {
            var sleep = Rescale(entry.SleepQuality);
            var health = Rescale(entry.HealthStatus);

            if (sleep == entry.SleepQuality && health == entry.HealthStatus) continue;

            entry.SleepQuality = sleep;
            entry.HealthStatus = health;
            entries.Update(entry);
        }
    }

    /// <summary>
    /// Halves an out-of-ten value onto the out-of-five scale, rounding a midpoint up so 7 becomes 4
    /// and 9 becomes 5 rather than silently sliding down.
    /// </summary>
    public static int Rescale(int value)
    {
        if (value <= DiaryEntry.MaxRating) return value;

        return (int)Math.Round(value / 2.0, MidpointRounding.AwayFromZero);
    }
}
