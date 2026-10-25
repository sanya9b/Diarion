using System;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class EmotionExtensionsTests : IDisposable
{
    private readonly CultureInfo? _originalCulture = AppResources.Culture;

    public void Dispose() => AppResources.Culture = _originalCulture;

    [Theory]
    [InlineData(Emotion.Happy, 2)]
    [InlineData(Emotion.Calm, 1)]
    [InlineData(Emotion.Anxious, -1)]
    [InlineData(Emotion.Sad, -2)]
    [InlineData(Emotion.Angry, -2)]
    [InlineData(Emotion.None, 0)]
    [InlineData(Emotion.Neutral, 0)]
    public void ToValence_KeepsTheOriginalFiveWhereTheyShipped(Emotion emotion, int expected)
    {
        // Stored trends, correlations and prompt thresholds all read these; none of them may move.
        emotion.ToValence().Should().Be(expected);
    }

    [Theory]
    [InlineData(Emotion.Happy, 1)]
    [InlineData(Emotion.Calm, -1)]
    [InlineData(Emotion.Anxious, 1)]
    [InlineData(Emotion.Sad, -1)]
    [InlineData(Emotion.Angry, 2)]
    [InlineData(Emotion.None, 0)]
    [InlineData(Emotion.Neutral, 0)]
    public void ToArousal_PlacesTheOriginalFiveOnTheEnergyAxis(Emotion emotion, int expected)
    {
        emotion.ToArousal().Should().Be(expected);
    }

    [Theory]
    [InlineData(Emotion.Happy, "#C26D53")]    // Coral, as from day one
    [InlineData(Emotion.Calm, "#8FA083")]     // Sage
    [InlineData(Emotion.Anxious, "#C9985A")]  // Amber
    [InlineData(Emotion.Sad, "#929FA7")]      // Ocean
    [InlineData(Emotion.Angry, "#C9985A")]    // Amber: off Berry, which goes back to the cycle
    [InlineData(Emotion.Stressed, "#C9985A")] // inherits its quadrant
    [InlineData(Emotion.Tired, "#929FA7")]
    [InlineData(Emotion.Neutral, "#6E6E6E")]
    [InlineData(Emotion.None, "#D0D3D4")]
    public void ToColorHex_IsTheColorOfTheQuadrant(Emotion emotion, string expected)
    {
        emotion.ToColorHex().Should().Be(expected);
    }

    [Fact]
    public void Every_emotion_sits_where_its_signs_say()
    {
        // Membership is declared in the catalogue, coordinates in the plane table. This is the seam
        // between them: a pleasant emotion with a negative valence would colour one way and count
        // the other.
        foreach (var emotion in EmotionCatalog.All)
        {
            var valence = emotion.ToValence();
            var arousal = emotion.ToArousal();

            Math.Abs(valence).Should().BeLessThanOrEqualTo(2, $"{emotion}");
            Math.Abs(arousal).Should().BeLessThanOrEqualTo(2, $"{emotion}");

            var expected = (valence, arousal) switch
            {
                ( > 0, > 0) => EmotionQuadrant.PleasantEnergetic,
                ( > 0, < 0) => EmotionQuadrant.PleasantCalm,
                ( < 0, > 0) => EmotionQuadrant.UnpleasantEnergetic,
                ( < 0, < 0) => EmotionQuadrant.UnpleasantCalm,
                (0, 0) => EmotionQuadrant.Neutral,
                _ => EmotionQuadrant.None
            };

            emotion.ToQuadrant().Should().Be(expected, $"{emotion} is at ({valence}, {arousal})");
        }
    }

    [Fact]
    public void None_is_off_the_plane()
    {
        Emotion.None.ToQuadrant().Should().Be(EmotionQuadrant.None);
        Emotion.None.ToValence().Should().Be(0);
        Emotion.None.ToArousal().Should().Be(0);
        EmotionCatalog.All.Should().NotContain(Emotion.None);
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("en")]
    public void Every_emotion_and_quadrant_has_its_own_name(string culture)
    {
        AppResources.Culture = new CultureInfo(culture);

        var names = EmotionCatalog.All.Select(e => e.ToLocalizedName()).ToList();
        names.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n));
        names.Should().OnlyHaveUniqueItems();
        names.Should().NotContain(Emotion.None.ToLocalizedName(),
            "an emotion missing from the name switch would fall through to the 'None' text");

        var quadrants = EmotionCatalog.Quadrants.Select(q => q.ToLocalizedName()).ToList();
        quadrants.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n));
        quadrants.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ukrainian_names_are_states_not_masculine_adjectives()
    {
        AppResources.Culture = new CultureInfo("uk");

        Emotion.Happy.ToLocalizedName().Should().Be("Радість");
        Emotion.Angry.ToLocalizedName().Should().Be("Злість");
        Emotion.Neutral.ToLocalizedName().Should().Be("Нейтрально");
    }
}
