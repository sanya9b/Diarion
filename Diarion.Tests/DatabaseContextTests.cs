using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The context around a real file: a copy taken while the database stays open, a file swap that
/// readers wait out instead of crashing into, and a migration that throws and leaves the file as it
/// was. Each of these was a production failure first.
/// </summary>
public class DatabaseContextTests : IDisposable
{
    private const string Key = "unit-test-device-key";
    private readonly string _dir;

    public DatabaseContextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_ctx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class FixedKey : IEncryptionKeyProvider
    {
        public string GetOrCreateKey() => Key;
    }

    private string NewPath() => Path.Combine(_dir, $"db_{Guid.NewGuid():N}.db");

    private static void WriteEntry(IDatabaseContext context, string content)
    {
        context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
            .Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = content });
    }

    private static string[] ReadContents(IDatabaseContext context) =>
        context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
            .FindAll().Select(e => e.Content).OrderBy(c => c).ToArray();

    // ---- ExportTo ------------------------------------------------------------

    [Fact]
    public void ExportTo_CopiesEveryDocumentAndTheSchemaVersion_WithoutClosingTheDatabase()
    {
        using var context = new DatabaseContext(NewPath(), keyProvider: new FixedKey());
        WriteEntry(context, "перший");
        WriteEntry(context, "другий");

        var copy = NewPath();
        context.ExportTo(copy, "backup-key");

        // Still open: the whole point. The old code closed it here and every reader threw.
        ReadContents(context).Should().Equal("другий", "перший");

        using var exported = new LiteDatabase(new ConnectionString { Filename = copy, Password = "backup-key" });
        exported.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).FindAll()
            .Select(e => e.Content).Should().BeEquivalentTo("перший", "другий");
        exported.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void ExportTo_RequiresAPassword()
    {
        using var context = new DatabaseContext(NewPath(), keyProvider: new FixedKey());
        var act = () => context.ExportTo(NewPath(), "");
        act.Should().Throw<ArgumentException>();
    }

    // ---- ReplaceDatabaseFile -------------------------------------------------

    [Fact]
    public void ReplaceDatabaseFile_SwapsInTheCandidate_AndRemovesIt()
    {
        var livePath = NewPath();
        using var context = new DatabaseContext(livePath, keyProvider: new FixedKey());
        WriteEntry(context, "старе");

        var candidate = NewPath();
        using (var other = new DatabaseContext(candidate, keyProvider: new FixedKey()))
        {
            WriteEntry(other, "відновлене");
        }

        context.ReplaceDatabaseFile(candidate);

        ReadContents(context).Should().Equal("відновлене");
        File.Exists(candidate).Should().BeFalse();
        File.Exists(livePath + ".importbak").Should().BeFalse();
        context.DatabasePath.Should().Be(livePath);
    }

    [Fact]
    public void ReplaceDatabaseFile_CandidateUnderAnotherKey_KeepsTheOldDatabase()
    {
        // The realistic bad candidate: a database this device's key does not open. (A file of plain
        // garbage is not it — LiteDB opens that without complaint and only fails on the first read.)
        var livePath = NewPath();
        using var context = new DatabaseContext(livePath, keyProvider: new FixedKey());
        WriteEntry(context, "старе");

        var candidate = NewPath();
        using (var foreign = new LiteDatabase(new ConnectionString { Filename = candidate, Password = "someone-else's-key" }))
        {
            foreign.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
                .Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = "чуже" });
            foreign.Checkpoint();
        }

        var act = () => context.ReplaceDatabaseFile(candidate);
        act.Should().Throw<Exception>();

        ReadContents(context).Should().Equal("старе");
        File.Exists(livePath + ".importbak").Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceDatabaseFile_ReadersDuringTheSwap_WaitInsteadOfThrowing()
    {
        // The crash this guards against: the scheduled backup (or a restore) had the file closed for
        // seconds, and a screen that loaded meanwhile got "database has been closed".
        var livePath = NewPath();
        using var context = new DatabaseContext(livePath, keyProvider: new FixedKey());
        WriteEntry(context, "старе");

        var candidate = NewPath();
        using (var other = new DatabaseContext(candidate, keyProvider: new FixedKey()))
        {
            WriteEntry(other, "відновлене");
        }

        using var stop = new CancellationTokenSource();
        var closedFailures = 0;
        var inFlight = 0;
        var reads = 0;
        var readsAfterLastSwap = 0;
        var swapping = true;
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    _ = context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).Count();
                    Interlocked.Increment(ref reads);
                    if (!Volatile.Read(ref swapping)) Interlocked.Increment(ref readsAfterLastSwap);
                }
                catch (InvalidOperationException)
                {
                    // "Database is not initialized or has been closed": the gap this test is about.
                    Interlocked.Increment(ref closedFailures);
                }
                catch (LiteException)
                {
                    // A read that had already taken its collection from the old instance when the
                    // swap disposed it. The window is the few microseconds between the two calls;
                    // what matters is that the next call goes through and reads the new file.
                    Interlocked.Increment(ref inFlight);
                }
            }
        });

        await Task.Delay(50);
        for (var i = 0; i < 3; i++)
        {
            var next = NewPath();
            File.Copy(candidate, next);
            context.ReplaceDatabaseFile(next);
        }
        Volatile.Write(ref swapping, false);
        await Task.Delay(50);
        stop.Cancel();
        await reader;

        reads.Should().BeGreaterThan(0);
        closedFailures.Should().Be(0, "a reader must wait out the swap, not fall into the gap");
        readsAfterLastSwap.Should().BeGreaterThan(0, "the reader keeps working on the new file");
        ReadContents(context).Should().Equal("відновлене");
    }

    // ---- Migration safety copy -----------------------------------------------

    private string DatabaseAtVersion(int version, string content)
    {
        var path = NewPath();
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = Key });
        db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
            .Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = content });
        db.UserVersion = version;
        db.Checkpoint();
        return path;
    }

    [Fact]
    public void Open_OlderSchema_MigratesAndLeavesNoSafetyCopyBehind()
    {
        var path = DatabaseAtVersion(MigrationRunner.CurrentVersion - 1, "запис");

        using var context = new DatabaseContext(path, keyProvider: new FixedKey());

        context.MigrationFailure.Should().BeNull();
        ReadContents(context).Should().Equal("запис");
        File.Exists(DatabaseContext.SafetyCopyPath(path, MigrationRunner.CurrentVersion - 1)).Should().BeFalse();
    }

    [Fact]
    public void Open_MigrationThrows_PutsTheFileBackAndStaysOpenOnTheOldSchema()
    {
        const int from = 3;
        var path = DatabaseAtVersion(from, "запис");
        using var context = new DatabaseContext(path, keyProvider: new FixedKey(), migrate: db =>
        {
            // Half a migration, then the failure: a bumped version and a written document that must
            // both disappear again.
            db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
                .Insert(new DiaryEntry { Id = Guid.NewGuid(), Date = DateTime.Today, Content = "напівмігроване" });
            db.UserVersion = from + 1;
            db.Checkpoint();
            throw new InvalidOperationException("міграція впала");
        });

        context.MigrationFailure.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("міграція впала");
        ReadContents(context).Should().Equal("запис");
        File.Exists(DatabaseContext.SafetyCopyPath(path, from)).Should().BeFalse();

        // The file on disk holds what the user came with and nothing of the failed run. (Not compared
        // byte for byte: LiteDB grows the file on reopen; a fresh, empty log next to it is its doing
        // on every open, while the one with the failed migration's pages is gone.)
        context.Close();
        using var db = new LiteDatabase(new ConnectionString { Filename = path, Password = Key });
        db.UserVersion.Should().Be(from);
        db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).FindAll()
            .Select(e => e.Content).Should().Equal("запис");
    }

    [Fact]
    public void Open_CurrentSchema_DoesNotWriteASafetyCopy()
    {
        var path = DatabaseAtVersion(MigrationRunner.CurrentVersion, "запис");
        var copied = false;

        using var context = new DatabaseContext(path, keyProvider: new FixedKey(), migrate: _ =>
        {
            copied = Directory.EnumerateFiles(_dir, "*.bak").Any();
        });

        copied.Should().BeFalse();
    }

    [Fact]
    public void LogFilePath_FollowsLiteDbNaming()
    {
        DatabaseContext.LogFilePath(Path.Combine("d", "diarion_local.db"))
            .Should().Be(Path.Combine("d", "diarion_local-log.db"));
    }
}
