using System;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Splits the habit schedule into the two things it was conflating: a recurrence rule (which days) and a
/// weekly quota (how many times). <c>TimesPerWeek</c> was never a recurrence — the old
/// <c>IsScheduledOn</c> answered <c>true</c> for it on every day and the strength calculator forked around
/// that — so it moves out to <c>Target</c> and the schedule becomes a plain <c>RecurrenceRule</c>.
///
/// Unlike every earlier migration this one works on raw <see cref="BsonDocument"/>s rather than the typed
/// model, because the typed read is the very thing that breaks: the enum is stored by name, and
/// <c>"SpecificDays"</c> and <c>"TimesPerWeek"</c> no longer exist on <c>RecurrenceKind</c>, so
/// deserializing a legacy habit would throw before the migration could touch it.
///
/// Idempotent per document, keyed on the absence of the legacy <c>Type</c> field. Per document rather than
/// per collection on purpose: the runner has no transaction, so a run interrupted halfway must be able to
/// resume and finish the rest.
/// </summary>
public sealed class M006_SplitHabitScheduleIntoRecurrence : IMigration
{
    public int ToVersion => 6;

    public void Up(LiteDatabase db)
    {
        var habits = db.GetCollection(DatabaseConstants.HabitDefinitionsCollection);

        foreach (var doc in habits.FindAll())
        {
            if (!doc.ContainsKey("Schedule") || !doc["Schedule"].IsDocument) continue;

            var schedule = doc["Schedule"].AsDocument;

            // Already migrated: the legacy discriminator is gone.
            if (!schedule.ContainsKey("Type")) continue;

            var legacyType = ReadLegacyType(schedule["Type"]);
            var storeAsString = schedule["Type"].IsString;

            switch (legacyType)
            {
                case LegacyScheduleType.SpecificDays:
                    schedule["Kind"] = Kind("Weekly", 1, storeAsString);
                    break;

                case LegacyScheduleType.TimesPerWeek:
                    // Open on every day, exactly as the old IsScheduledOn answered, with the quota moved out.
                    schedule["Kind"] = Kind("Daily", 0, storeAsString);
                    doc["Target"] = new BsonDocument
                    {
                        ["TimesPerWeek"] = Math.Clamp(ReadTimesPerWeek(schedule), 1, 7)
                    };
                    break;

                default:
                    schedule["Kind"] = Kind("Daily", 0, storeAsString);
                    break;
            }

            schedule.Remove("Type");
            schedule.Remove("TimesPerWeek");

            // Anchor is deliberately left unwritten so it deserializes to DateTime.MinValue, i.e. no lower
            // bound. Seeding it from CreatedAt would look tidier and would silently change the answer for
            // every date before the habit existed, which is what strength and streak walk over.

            habits.Update(doc);
        }
    }

    private enum LegacyScheduleType { Daily, SpecificDays, TimesPerWeek }

    /// <summary>
    /// Reads the legacy discriminator whichever way it was written. LiteDB stores enums by name under the
    /// default mapper, but a database that was ever written with <c>EnumAsInteger</c> would hold 0/1/2, and
    /// guessing wrong here turns a quota habit into an every-Nth-day one with nothing to show for it.
    /// </summary>
    private static LegacyScheduleType ReadLegacyType(BsonValue value)
    {
        if (value.IsString)
        {
            return Enum.TryParse<LegacyScheduleType>(value.AsString, ignoreCase: true, out var parsed)
                ? parsed
                : LegacyScheduleType.Daily;
        }

        if (value.IsNumber)
        {
            return value.AsInt32 switch
            {
                1 => LegacyScheduleType.SpecificDays,
                2 => LegacyScheduleType.TimesPerWeek,
                _ => LegacyScheduleType.Daily
            };
        }

        return LegacyScheduleType.Daily;
    }

    private static int ReadTimesPerWeek(BsonDocument schedule)
        => schedule.ContainsKey("TimesPerWeek") && schedule["TimesPerWeek"].IsNumber
            ? schedule["TimesPerWeek"].AsInt32
            : 3;

    /// <summary>Writes the new discriminator in the same representation the old one used, so the document
    /// stays consistent with however this database's mapper reads enums back.</summary>
    private static BsonValue Kind(string name, int ordinal, bool asString)
        => asString ? new BsonValue(name) : new BsonValue(ordinal);
}
