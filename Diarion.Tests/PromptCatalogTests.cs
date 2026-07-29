using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The catalog is now only the seed manifest, so these assert the seed <em>input</em>: that every key
/// exists and has real text in both languages. Once seeded, that text is copied into the database and
/// never re-read, which is exactly why a gap here would be permanent.
/// </summary>
public class PromptCatalogTests
{
    [Theory]
    [InlineData(PromptCategory.CbtReframe)]
    [InlineData(PromptCategory.Savouring)]
    [InlineData(PromptCategory.OpenReflection)]
    [InlineData(PromptCategory.EveningGratitude)]
    public void EveryCategoryHasTenPrompts(PromptCategory category)
    {
        PromptCatalog.SeedKeys[category].Should().HaveCount(10);
    }

    [Fact]
    public void KeysAreUniqueAcrossCategories()
    {
        var all = PromptCatalog.AllKeys.ToList();

        all.Should().OnlyHaveUniqueItems();
        all.Should().HaveCount(40);
    }

    [Theory]
    [InlineData("")]
    [InlineData("uk")]
    public void EveryPromptHasTextInBothSeedCultures(string cultureName)
    {
        // Read by explicit culture, the same way the seeder does — the ambient culture is not set up yet
        // when seeding runs.
        var culture = cultureName.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(cultureName);

        foreach (var key in PromptCatalog.AllKeys)
        {
            var text = AppResources.ResourceManager.GetString(key, culture);

            text.Should().NotBeNullOrWhiteSpace($"prompt '{key}' must exist in '{culture.Name}' before it is seeded");
        }
    }

    [Fact]
    public void PromptTextDiffersBetweenLanguages()
    {
        var key = PromptCatalog.SeedKeys[PromptCategory.CbtReframe][0];

        var english = AppResources.ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        var ukrainian = AppResources.ResourceManager.GetString(key, new CultureInfo("uk"));

        // A missing Ukrainian satellite would silently fall back to English and freeze that into every
        // user's database on first run, where no later fix can reach it.
        ukrainian.Should().NotBe(english);
    }
}
