using System;
using System.Linq;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class EmotionCatalogTests
{
    [Fact]
    public void Every_emotion_except_None_is_filed_exactly_once()
    {
        var recordable = Enum.GetValues<Emotion>().Where(e => e != Emotion.None).ToList();

        EmotionCatalog.All.Should().OnlyHaveUniqueItems();
        EmotionCatalog.All.Should().BeEquivalentTo(recordable,
            "an emotion the picker cannot reach, or lists twice, is a defect in the catalogue");
    }

    [Theory]
    [InlineData(EmotionQuadrant.PleasantEnergetic, Emotion.Happy)]
    [InlineData(EmotionQuadrant.PleasantCalm, Emotion.Calm)]
    [InlineData(EmotionQuadrant.UnpleasantEnergetic, Emotion.Anxious)]
    [InlineData(EmotionQuadrant.UnpleasantCalm, Emotion.Sad)]
    [InlineData(EmotionQuadrant.Neutral, Emotion.Neutral)]
    [InlineData(EmotionQuadrant.None, Emotion.None)]
    public void Representative_is_the_first_emotion_of_the_quadrant(EmotionQuadrant quadrant, Emotion expected)
    {
        // A bare quadrant tap records one of the original five (or Neutral), so a day logged in the
        // new picker without refinement is indistinguishable from one logged before it existed.
        quadrant.Representative().Should().Be(expected);

        if (quadrant != EmotionQuadrant.None)
        {
            quadrant.Emotions()[0].Should().Be(expected);
        }
    }

    [Fact]
    public void QuadrantOf_round_trips_membership()
    {
        foreach (var quadrant in EmotionCatalog.Quadrants)
        {
            foreach (var emotion in quadrant.Emotions())
            {
                emotion.ToQuadrant().Should().Be(quadrant);
            }
        }

        EmotionQuadrant.None.Emotions().Should().BeEmpty();
    }

    [Fact]
    public void Each_quadrant_offers_seven_emotions_and_Neutral_only_itself()
    {
        foreach (var quadrant in EmotionCatalog.Quadrants.Where(q => q != EmotionQuadrant.Neutral))
        {
            quadrant.Emotions().Should().HaveCount(7, $"{quadrant} should fit in two rows of chips");
        }

        EmotionQuadrant.Neutral.Emotions().Should().Equal(Emotion.Neutral);
    }

    [Fact]
    public void Quadrant_colours_are_distinct_and_leave_Berry_to_the_cycle()
    {
        var colours = EmotionCatalog.Quadrants.Select(q => q.ToColorHex()).ToList();

        colours.Should().OnlyHaveUniqueItems();
        colours.Should().NotContain("#A87C8E", "Berry is the cycle's colour");
        colours.Should().NotContain(EmotionQuadrant.None.ToColorHex(), "nothing recorded must not look like a quadrant");
    }

    [Fact]
    public void Recordable_quadrants_have_distinct_theme_keys()
    {
        // The mood row highlights through these keys so each theme can restyle them; every theme
        // dictionary must define all five (see Resources/Themes).
        var keys = EmotionCatalog.Quadrants.Select(q => q.ToThemeKey()).ToList();

        keys.Should().OnlyHaveUniqueItems();
        keys.Should().OnlyContain(k => k.StartsWith("Theme_Mood"));
        EmotionQuadrant.None.ToThemeKey().Should().NotStartWith("Theme_Mood", "nothing recorded is not a highlight");
    }

    [Fact]
    public void Recordable_quadrants_have_distinct_face_images()
    {
        // MAUI image names: lowercase letters, digits and underscores, starting with a letter.
        var files = EmotionCatalog.Quadrants.Select(q => q.ToIconFile()).ToList();

        files.Should().OnlyHaveUniqueItems();
        files.Should().OnlyContain(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^[a-z][a-z0-9_]*\.png$"));
        EmotionQuadrant.None.ToIconFile().Should().BeEmpty();
    }

    [Fact]
    public void Recordable_quadrants_have_a_glyph()
    {
        EmotionCatalog.Quadrants.Select(q => q.ToEmoji()).Should().OnlyContain(e => !string.IsNullOrEmpty(e));
        EmotionCatalog.Quadrants.Select(q => q.ToEmoji()).Should().OnlyHaveUniqueItems();
        EmotionQuadrant.None.ToEmoji().Should().BeEmpty();
    }
}
