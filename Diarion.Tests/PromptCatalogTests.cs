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
