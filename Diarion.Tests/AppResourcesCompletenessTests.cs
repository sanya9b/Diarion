using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Diarion.Resources.Localization;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// English used to live in two places: the resx, and a hand-written <c>?? "…"</c> fallback on each
/// generated property. Forty strings existed only in the second, so regenerating a file marked
/// <c>&lt;auto-generated&gt;</c> would have silently emptied them, and one had already drifted out of
/// step with its resx twin.
/// <para>
/// These read the compiled resource sets with <c>tryParents: false</c>, which is the whole trick. Going
/// through the properties instead proves nothing about Ukrainian: <c>ResourceManager</c> falls back to
/// the neutral resx, so a missing translation quietly returns the English string and every assertion
/// passes. Turning the fallback off is what makes an untranslated key visible.
/// </para>
/// </summary>
public class AppResourcesCompletenessTests
{
    private static HashSet<string> KeysIn(CultureInfo culture)
    {
        var set = AppResources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull($"the resources for '{culture.Name}' must be compiled into the assembly");

        return set!.Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet();
    }

    private static HashSet<string> English => KeysIn(CultureInfo.InvariantCulture);
    private static HashSet<string> Ukrainian => KeysIn(new CultureInfo("uk"));

    [Fact]
    public void Every_ukrainian_string_has_an_english_one()
    {
        // The direction that actually broke: forty keys were Ukrainian-only, and their English text
        // survived solely as a literal in generated code.
        Ukrainian.Except(English).Should().BeEmpty("every key must also exist in AppResources.resx");
    }

    [Fact]
    public void Every_english_string_has_a_ukrainian_one()
    {
        // The opposite drift, and the likelier one from here: a new key added in English and never
        // translated shows up as English text in a Ukrainian interface, which nothing else reports.
        English.Except(Ukrainian).Should().BeEmpty("every key must also exist in AppResources.uk.resx");
    }

    [Fact]
    public void No_string_is_blank_in_either_language()
    {
        var blank = new List<string>();
        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("uk") })
        {
            var set = AppResources.ResourceManager.GetResourceSet(culture, true, tryParents: false)!;
            blank.AddRange(set.Cast<DictionaryEntry>()
                .Where(e => e.Value is null or "")
                .Select(e => $"{culture.Name}:{e.Key}"));
        }

        blank.Should().BeEmpty();
    }

    [Fact]
    public void The_generated_accessors_cover_the_resources()
    {
        // Guards the guards: if reflection or the resource sets ever come back empty, everything above
        // would pass by vacuum rather than by correctness.
        var properties = typeof(AppResources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Count(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0);

        properties.Should().BeGreaterThan(400);
        English.Should().HaveCountGreaterThan(400);
        Ukrainian.Should().HaveCountGreaterThan(400);
    }
}
