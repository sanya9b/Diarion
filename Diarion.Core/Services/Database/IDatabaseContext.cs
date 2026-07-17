using LiteDB;

namespace Diarion.Services.Database;

public interface IDatabaseContext
{
    ILiteCollection<T> GetCollection<T>(string name);
    string DatabasePath { get; }
    void Close();

    /// <summary>
    /// Re-opens the database after a <see cref="Close"/> (e.g. after a backup import swapped the
    /// underlying file). Safe to call whether or not the database is currently open.
    /// </summary>
    void Reopen();

    void DropAllData();
}
