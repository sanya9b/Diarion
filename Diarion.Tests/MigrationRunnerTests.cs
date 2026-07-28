using System;
using System.IO;
using Diarion.Models;
using Diarion.Services.Database;
using Diarion.Services.Database.Migrations;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class MigrationRunnerTests
{
    [Fact]
    public void Run_FreshDatabase_SetsCurrentVersion()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.UserVersion.Should().Be(0);

        MigrationRunner.Run(db);

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_NormalizesDiaryDatesToMidnight_AndSetsVersion()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var id = Guid.NewGuid();
        entries.Insert(new DiaryEntry { Id = id, Date = new DateTime(2026, 1, 10, 14, 30, 0) });

        MigrationRunner.Run(db);

        var migrated = entries.FindById(id);
        migrated.Date.TimeOfDay.Should().Be(TimeSpan.Zero);
        migrated.Date.Date.Should().Be(new DateTime(2026, 1, 10));
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_BackfillsNoteTagsAndLinks_FromLegacyContent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var notes = db.GetCollection<Note>(DatabaseConstants.NotesCollection);
        // A note created before tag/link parsing existed: content has them, but the fields are empty.
        var note = new Note { Title = "Legacy", Content = "Idea #spark linking [[Other Note]]" };
        note.Tags = new();
        note.LinkedTitles = new();
        notes.Insert(note);

        MigrationRunner.Run(db);

        var migrated = notes.FindById(note.Id);
        migrated.Tags.Should().Equal("spark");
        migrated.LinkedTitles.Should().Equal("other note");
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_CreatesDefaultAccount_AndBackfillsTransactionAccountId()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10) };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        var account = db.GetCollection<Account>(DatabaseConstants.AccountsCollection).FindAll().Should().ContainSingle().Subject;
        // The name is stored as a resource key so it follows the UI language rather than whatever
        // culture happened to be active when the database was first migrated.
        account.ResourceKey.Should().Be("DefaultAccountName");
        transactions.FindById(tx.Id).AccountId.Should().Be(account.Id);
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_WithExistingAccount_ReusesItInsteadOfCreatingAnother()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var accounts = db.GetCollection<Account>(DatabaseConstants.AccountsCollection);
        var existing = new Account { Name = "Cash", CreatedAt = new DateTime(2025, 1, 1) };
        accounts.Insert(existing);

        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10) };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        accounts.FindAll().Should().ContainSingle().Which.Id.Should().Be(existing.Id);
        transactions.FindById(tx.Id).AccountId.Should().Be(existing.Id);
    }

    [Fact]
    public void Run_DoesNotReassignTransactionsThatAlreadyHaveAnAccount()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var accounts = db.GetCollection<Account>(DatabaseConstants.AccountsCollection);
        var first = new Account { Name = "First", CreatedAt = new DateTime(2025, 1, 1) };
        var second = new Account { Name = "Second", CreatedAt = new DateTime(2025, 6, 1) };
        accounts.InsertBulk(new[] { first, second });

        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10), AccountId = second.Id };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        transactions.FindById(tx.Id).AccountId.Should().Be(second.Id);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]   // already on the new scale — ambiguous, so left alone
    [InlineData(6, 3)]
    [InlineData(7, 4)]   // midpoint rounds up
    [InlineData(8, 4)]
    [InlineData(9, 5)]   // midpoint rounds up
    [InlineData(10, 5)]
    public void Rescale_HalvesOnlyValuesAboveTheNewMaximum(int stored, int expected)
    {
        M004_NormalizeRatingScales.Rescale(stored).Should().Be(expected);
    }

    [Fact]
    public void Run_NormalizesOutOfTenRatings_AndLeavesValidOnesAlone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

        var legacy = new DiaryEntry { Date = new DateTime(2026, 1, 10), SleepQuality = 9, HealthStatus = 7 };
        var current = new DiaryEntry { Date = new DateTime(2026, 1, 11), SleepQuality = 4, HealthStatus = 5 };
        entries.Insert(legacy);
        entries.Insert(current);

        MigrationRunner.Run(db);

        var migrated = entries.FindById(legacy.Id);
        migrated.SleepQuality.Should().Be(5);
        migrated.HealthStatus.Should().Be(4);

        var untouched = entries.FindById(current.Id);
        untouched.SleepQuality.Should().Be(4);
        untouched.HealthStatus.Should().Be(5);
    }

    [Fact]
    public void Run_RatingNormalization_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var entry = new DiaryEntry { Date = new DateTime(2026, 1, 10), SleepQuality = 10 };
        entries.Insert(entry);

        MigrationRunner.Run(db);
        MigrationRunner.Run(db);

        // A second pass must not halve the already-halved value down to 3.
        entries.FindById(entry.Id).SleepQuality.Should().Be(5);
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
          .Insert(new DiaryEntry { Date = new DateTime(2026, 1, 10, 9, 0, 0) });

        MigrationRunner.Run(db);
        MigrationRunner.Run(db); // second run must be a no-op

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_DoesNotDowngradeNewerDatabase()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.UserVersion = MigrationRunner.CurrentVersion + 5; // simulate a future app schema

        MigrationRunner.Run(db);

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion + 5);
    }
}
