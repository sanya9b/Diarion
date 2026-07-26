using System.Linq;
using Diarion.Services.Database.Migrations;
using LiteDB;

namespace Diarion.Services.Database;

/// <summary>
/// Applies pending schema migrations based on LiteDB's <see cref="LiteDatabase.UserVersion"/>.
/// Runs on every startup; a no-op once the database is already at <see cref="CurrentVersion"/>.
/// Never downgrades (a database from a newer app version is left untouched).
/// </summary>
public static class MigrationRunner
{
    public const int CurrentVersion = 3;

    private static readonly IMigration[] Migrations =
    {
        new M001_NormalizeDiaryDates(),
        new M002_BackfillNoteTagsAndLinks(),
        new M003_BackfillDefaultAccount(),
    };

    public static void Run(LiteDatabase db)
    {
        var from = db.UserVersion;
        if (from >= CurrentVersion)
        {
            return;
        }

        foreach (var migration in Migrations.Where(m => m.ToVersion > from).OrderBy(m => m.ToVersion))
        {
            migration.Up(db);
            db.UserVersion = migration.ToVersion;
        }
    }
}
