using System;
using LiteDB;

namespace Diarion.Services.Database;

public interface IDatabaseContext
{
    /// <summary>
    /// Waits while the file is being replaced (<see cref="ReplaceDatabaseFile"/>) rather than throwing,
    /// so a screen that loads during a restore sees the restored data instead of a dead process.
    /// </summary>
    ILiteCollection<T> GetCollection<T>(string name);

    string DatabasePath { get; }

    void Close();

    /// <summary>
    /// Re-opens the database after a <see cref="Close"/>. Safe to call whether or not the database is
    /// currently open.
    /// </summary>
    void Reopen();

    void DropAllData();

    /// <summary>
    /// Writes a copy of the open database to <paramref name="destinationPath"/> under
    /// <paramref name="password"/>, through the live connection. The database stays open for everyone
    /// else the whole time: closing it for a copy is what let a screen load into a closed database.
    /// </summary>
    void ExportTo(string destinationPath, string password);

    /// <summary>
    /// Swaps the file at <see cref="DatabasePath"/> for <paramref name="candidatePath"/> and reopens,
    /// as one step during which every <see cref="GetCollection{T}"/> waits. The previous file is put
    /// back if the candidate cannot be opened. The candidate is consumed on success.
    /// </summary>
    void ReplaceDatabaseFile(string candidatePath);

    /// <summary>
    /// The exception the schema migration threw at the last open, after the file was put back the way
    /// it was. Null when every migration applied. The app keeps running on the old schema; this is
    /// how it says so.
    /// </summary>
    Exception? MigrationFailure { get; }
}
