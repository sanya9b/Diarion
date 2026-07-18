using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// A single, ordered, idempotent schema migration applied to bring the database up to
/// <see cref="ToVersion"/>. Migrations must be safe to re-run.
/// </summary>
public interface IMigration
{
    int ToVersion { get; }
    void Up(LiteDatabase db);
}
