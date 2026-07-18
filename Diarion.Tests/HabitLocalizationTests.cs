using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class HabitLocalizationTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly HabitService _habitService;
    private readonly CultureInfo? _originalCulture;

    public HabitLocalizationTests()
    {
        _originalCulture = AppResources.Culture;
        _dbContext = new DatabaseContext(useInMemory: true);
        _habitService = new HabitService(_dbContext);
    }

    public void Dispose()
    {
        AppResources.Culture = _originalCulture;
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetActiveHabitsForDateAsync_ResolvesDefaultHabitNameToCurrentLanguage()
    {
        var habits = _dbContext.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
        habits.Insert(new HabitDefinition
        {
            Name = "Water",
            ResourceKey = "HabitWater",
            CreatedAt = DateTime.MinValue
        });

        AppResources.Culture = new CultureInfo("uk");
        var uk = await _habitService.GetActiveHabitsForDateAsync(DateTime.Today);
        uk.Single().Name.Should().Be("Вода");

        AppResources.Culture = new CultureInfo("en");
        var en = await _habitService.GetActiveHabitsForDateAsync(DateTime.Today);
        en.Single().Name.Should().Be("Water");
    }

    [Fact]
    public async Task GetActiveHabitsForDateAsync_KeepsUserHabitNameUnchanged()
    {
        var habits = _dbContext.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);
        habits.Insert(new HabitDefinition { Name = "Meditation", CreatedAt = DateTime.MinValue });

        AppResources.Culture = new CultureInfo("uk");
        var result = await _habitService.GetActiveHabitsForDateAsync(DateTime.Today);

        result.Single().Name.Should().Be("Meditation");
    }

    [Fact]
    public void Seeder_BackfillsResourceKey_ForLegacyDefaultHabit()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var habits = db.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection);

        // Legacy row: a default habit stored with an English literal name and no ResourceKey.
        var legacy = new HabitDefinition { Name = "Water", CreatedAt = DateTime.MinValue };
        habits.Insert(legacy);

        new DatabaseSeeder().Seed(db);

        var migrated = habits.FindById(legacy.Id);
        migrated.ResourceKey.Should().Be("HabitWater");
    }
}
