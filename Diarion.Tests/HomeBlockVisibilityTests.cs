using System;
using System.IO;
using System.Linq;
using Diarion.Models;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class HomeBlockVisibilityTests
{
    [Fact]
    public void NewProfile_HasAllHomeBlocksVisibleByDefault()
    {
        var profile = new UserProfile();

        profile.IsMoodBlockVisible.Should().BeTrue();
        profile.IsSleepBlockVisible.Should().BeTrue();
        profile.IsHealthBlockVisible.Should().BeTrue();
        profile.IsFoodBlockVisible.Should().BeTrue();
        profile.IsHabitsBlockVisible.Should().BeTrue();
        profile.IsReflectionBlockVisible.Should().BeTrue();
    }

    [Fact]
    public void LegacyProfile_WithoutVisibilityFields_DeserializesToAllVisible()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var raw = db.GetCollection(DatabaseConstants.ProfileCollection);

        // Simulate a profile stored before the block-visibility fields existed.
        raw.Insert(new BsonDocument
        {
            ["_id"] = Guid.NewGuid(),
            ["Name"] = "Legacy user",
            ["AutoMigrateUncompletedTasksEnabled"] = true
        });

        var profile = db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection).FindAll().Single();

        profile.Name.Should().Be("Legacy user");
        profile.IsMoodBlockVisible.Should().BeTrue();
        profile.IsSleepBlockVisible.Should().BeTrue();
        profile.IsHealthBlockVisible.Should().BeTrue();
        profile.IsFoodBlockVisible.Should().BeTrue();
        profile.IsHabitsBlockVisible.Should().BeTrue();
        profile.IsReflectionBlockVisible.Should().BeTrue();
    }
}
