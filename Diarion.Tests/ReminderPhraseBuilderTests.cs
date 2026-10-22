using System;
using System.Globalization;
using Diarion.Resources.Localization;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Spec 19: the words a spoken reminder says. Pinned because two platforms read the same phrase through
/// different mouths, and because iOS drops a notification sound past thirty seconds without a word.
/// </summary>
public class ReminderPhraseBuilderTests : IDisposable
{
    private readonly CultureInfo? _previous = AppResources.Culture;

    public ReminderPhraseBuilderTests() => AppResources.Culture = new CultureInfo("uk");

    public void Dispose() => AppResources.Culture = _previous;

    [Fact]
    public void Build_PrefixesTheTaskInTheAppLanguage()
    {
        ReminderPhraseBuilder.Build("Сходити в магазин").Should().Be("Нагадування: Сходити в магазин");
    }

    [Fact]
    public void Build_WithNothingToSay_FallsBackToTheBareWord()
    {
        ReminderPhraseBuilder.Build("   ").Should().Be("Нагадування");
        ReminderPhraseBuilder.Build(null).Should().Be("Нагадування");
        ReminderPhraseBuilder.Build("🛒🛒").Should().Be("Нагадування");
    }

    [Fact]
    public void Clean_DropsEmojiAndCollapsesWhitespace()
    {
        ReminderPhraseBuilder.Clean("Забрати 👧 дитину\n\n зі   школи 🏫").Should().Be("Забрати дитину зі школи");
    }

    [Fact]
    public void Clean_KeepsLettersPunctuationAndNumbers()
    {
        ReminderPhraseBuilder.Clean("Ліки о 15:30, 2 таблетки — не забути!").Should().Be("Ліки о 15:30, 2 таблетки — не забути!");
    }

    [Fact]
    public void Clean_CutsALongTaskAtAWordBoundary()
    {
        var word = "слово ";
        var longText = string.Concat(System.Linq.Enumerable.Repeat(word, 60)).Trim();

        var cleaned = ReminderPhraseBuilder.Clean(longText);

        cleaned.Length.Should().BeLessThanOrEqualTo(ReminderPhraseBuilder.MaxTaskLength);
        cleaned.Should().EndWith("слово");
        cleaned.Should().NotEndWith(" ");
    }

    [Fact]
    public void Clean_CutsHardWhenThereIsNoBoundaryToCutAt()
    {
        var unbroken = new string('а', ReminderPhraseBuilder.MaxTaskLength * 2);

        ReminderPhraseBuilder.Clean(unbroken).Length.Should().Be(ReminderPhraseBuilder.MaxTaskLength);
    }
}
