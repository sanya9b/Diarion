using System;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PromptCatalogTests : IDisposable
{
    private readonly CultureInfo? _originalCulture;

    public PromptCatalogTests() => _originalCulture = AppResources.Culture;

    public void Dispose() => AppResources.Culture = _originalCulture;

    [Theory]
    [InlineData(PromptCategory.CbtReframe)]
    [InlineData(PromptCategory.Savouring)]
    [InlineData(PromptCategory.OpenReflection)]
    [InlineData(PromptCategory.EveningGratitude)]
    public void EveryCategoryHasTenPrompts(PromptCategory category)
    {
        PromptCatalog.KeysFor(category).Should().HaveCount(10);
    }

    [Fact]
    public void KeysAreUniqueAcrossCategories()
    {
        var all = PromptCatalog.AllKeys.ToList();

        all.Should().OnlyHaveUniqueItems();
        all.Should().HaveCount(40);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("uk")]
    public void EveryPromptResolvesToRealTextInBothLanguages(string cultureName)
    {
        AppResources.Culture = new CultureInfo(cultureName);

        foreach (var key in PromptCatalog.AllKeys)
        {
            var text = PromptCatalog.ResolveText(key);

            // ResolveText falls back to the key when a resource is missing, so equality means a gap.
            text.Should().NotBe(key, $"prompt '{key}' must be translated into '{cultureName}'");
            text.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void PromptTextDiffersBetweenLanguages()
    {
        var key = PromptCatalog.KeysFor(PromptCategory.CbtReframe)[0];

        AppResources.Culture = new CultureInfo("en");
        var english = PromptCatalog.ResolveText(key);

        AppResources.Culture = new CultureInfo("uk");
        var ukrainian = PromptCatalog.ResolveText(key);

        // Guards against a Ukrainian entry silently missing and falling back to the English resource.
        ukrainian.Should().NotBe(english);
    }

    [Theory]
    [InlineData(PromptCategory.CbtReframe)]
    [InlineData(PromptCategory.Savouring)]
    [InlineData(PromptCategory.OpenReflection)]
    [InlineData(PromptCategory.EveningGratitude)]
    public void CategoryOf_RecognisesItsOwnKeys(PromptCategory category)
    {
        foreach (var key in PromptCatalog.KeysFor(category))
        {
            PromptCatalog.CategoryOf(key).Should().Be(category);
        }
    }

    [Fact]
    public void CategoryOf_UnknownOrEmptyKey_IsNull()
    {
        PromptCatalog.CategoryOf("nope").Should().BeNull();
        PromptCatalog.CategoryOf(null).Should().BeNull();
    }

    [Fact]
    public void Next_AdvancesWithinTheCategoryAndWrapsAround()
    {
        var keys = PromptCatalog.KeysFor(PromptCategory.Savouring);

        PromptCatalog.Next(keys[0]).Should().Be(keys[1]);
        PromptCatalog.Next(keys[^1]).Should().Be(keys[0]);
    }

    [Fact]
    public void Next_VisitsEveryPromptBeforeRepeating()
    {
        var keys = PromptCatalog.KeysFor(PromptCategory.OpenReflection);
        var visited = new System.Collections.Generic.List<string>();

        var current = keys[0];
        for (var i = 0; i < keys.Count; i++)
        {
            visited.Add(current);
            current = PromptCatalog.Next(current);
        }

        visited.Should().BeEquivalentTo(keys);
        current.Should().Be(keys[0]);
    }

    [Fact]
    public void Next_UnknownKey_IsReturnedUnchanged()
    {
        PromptCatalog.Next("nope").Should().Be("nope");
    }

    [Fact]
    public void ResolveText_UnknownKey_ReturnsTheKey()
    {
        PromptCatalog.ResolveText("PromptDoesNotExist").Should().Be("PromptDoesNotExist");
    }

    [Fact]
    public void ResolveText_EmptyKey_ReturnsEmpty()
    {
        PromptCatalog.ResolveText(null).Should().BeEmpty();
        PromptCatalog.ResolveText(string.Empty).Should().BeEmpty();
    }
}
