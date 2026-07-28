using System;
using System.IO;
using System.Linq;
using Diarion.Models;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Hourly mood was modelled long before anything wrote it, so no code path had ever proven it
/// survives a save and reload. These tests are that proof.
/// </summary>
public class DiaryEntryPersistenceTests
{
    [Fact]
    public void HourlyMood_RoundTripsThroughLiteDb()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

        var entry = new DiaryEntry
        {
            Date = new DateTime(2026, 7, 15),
            HourlyMood =
            {
                new HourMood { Hour = 7, Mood = Emotion.Calm },
                new HourMood { Hour = 14, Mood = Emotion.Anxious },
                new HourMood { Hour = 23, Mood = Emotion.Happy }
            }
        };
        entries.Insert(entry);

        var loaded = entries.FindById(entry.Id);

        loaded.HourlyMood.Should().HaveCount(3);
        loaded.HourlyMood.Select(h => h.Hour).Should().Equal(7, 14, 23);
        loaded.HourlyMood.Select(h => h.Mood).Should().Equal(Emotion.Calm, Emotion.Anxious, Emotion.Happy);
    }

    [Fact]
    public void HourlyMood_EmptyByDefault_AndSurvivesAsEmpty()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

        var entry = new DiaryEntry { Date = new DateTime(2026, 7, 15) };
        entries.Insert(entry);

        entries.FindById(entry.Id).HourlyMood.Should().BeEmpty();
    }

    [Fact]
    public void LegacyEntry_WithoutTheField_DeserializesToEmpty()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var raw = db.GetCollection(DatabaseConstants.EntriesCollection);

        // An entry stored before hourly mood existed in this shape.
        var id = Guid.NewGuid();
        raw.Insert(new BsonDocument
        {
            ["_id"] = id,
            ["Date"] = new DateTime(2026, 7, 15),
            ["Emotion"] = "Happy"
        });

        var loaded = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).FindById(id);

        loaded.Emotion.Should().Be(Emotion.Happy);
        loaded.HourlyMood.Should().NotBeNull().And.BeEmpty();
    }
}
