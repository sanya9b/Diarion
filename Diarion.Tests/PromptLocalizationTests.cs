using System;
using System.Globalization;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PromptLocalizationTests : IDisposable
{
    private readonly CultureInfo? _originalCulture;

    public PromptLocalizationTests() => _originalCulture = AppResources.Culture;

    public void Dispose() => AppResources.Culture = _originalCulture;

    private static GuidedPrompt Both() => new() { TextUk = "українською", TextEn = "in English" };

    [Fact]
    public void UkrainianCulture_PrefersTheUkrainianText()
    {
        AppResources.Culture = new CultureInfo("uk");

        PromptLocalization.ResolveText(Both()).Should().Be("українською");
    }

    [Fact]
    public void OtherCulture_PrefersTheEnglishText()
    {
        AppResources.Culture = new CultureInfo("en");

        PromptLocalization.ResolveText(Both()).Should().Be("in English");
    }

    [Fact]
    public void OnlyOneLanguageWritten_FallsBackToIt()
    {
        // A user writes their own prompt in one language and then switches the UI to the other.
        AppResources.Culture = new CultureInfo("en");
        PromptLocalization.ResolveText(new GuidedPrompt { TextUk = "лише українською" })
            .Should().Be("лише українською");

        AppResources.Culture = new CultureInfo("uk");
        PromptLocalization.ResolveText(new GuidedPrompt { TextEn = "English only" })
            .Should().Be("English only");
    }

    [Fact]
    public void BlankSeededRow_FallsBackToItsResource()
    {
        AppResources.Culture = new CultureInfo("en");

        var text = PromptLocalization.ResolveText(new GuidedPrompt { ResourceKey = "PromptCbt01" });

        text.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBe("PromptCbt01");
    }

    [Fact]
    public void DeletedPrompt_StillResolves()
    {
        AppResources.Culture = new CultureInfo("en");
        var prompt = Both();
        prompt.DeletedAt = DateTime.Today;

        // An entry that answered this prompt must still be able to show the question.
        PromptLocalization.ResolveText(prompt).Should().Be("in English");
    }

    [Fact]
    public void Null_IsEmpty()
    {
        PromptLocalization.ResolveText(null).Should().BeEmpty();
    }
}
