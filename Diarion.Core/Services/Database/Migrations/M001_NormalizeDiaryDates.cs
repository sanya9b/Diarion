using System;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Strips any time component from <see cref="DiaryEntry.Date"/> so per-day lookups and the
/// (non-unique) date index behave consistently. Idempotent: rows already at midnight are skipped.
/// </summary>
public sealed class M001_NormalizeDiaryDates : IMigration
{
    public int ToVersion => 1;

    public void Up(LiteDatabase db)
    {
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        foreach (var entry in entries.FindAll())
        {
            if (entry.Date.TimeOfDay != TimeSpan.Zero)
            {
                entry.Date = entry.Date.Date;
                entries.Update(entry);
            }
        }
    }
}
