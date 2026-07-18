using System;
using System.IO;
using Diarion.Models;
using Diarion.Services.Database;
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
