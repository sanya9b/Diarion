using System.Collections.Generic;
using Diarion.Resources.Localization;

namespace Diarion.Models;

/// <summary>
/// What a quadrant looks like and is called. The colour lives here rather than per emotion so the four
/// brand accents go exactly one each to the four quadrants, and every chart that colours by emotion
/// stays readable with a legend of four.
/// </summary>
public static class EmotionQuadrantExtensions
{
    /// <summary>
    /// Fixed brand hue per quadrant. Coral, Sage, Amber and Ocean are the palette's four accents in the
    /// roles they already play: Coral is Happy's colour from day one, Sage is Calm's, Amber is Anxious's
    /// and Ocean is Sad's. Angry moves from Berry to Amber because it shares a quadrant with Anxious,
    /// which also hands Berry back to the cycle, where the palette says it belongs. Neutral is a plain
    /// grey (the palette's Gray500): darker than Ocean so the two stay apart by lightness, not hue.
    /// </summary>
    public static string ToColorHex(this EmotionQuadrant quadrant) => quadrant switch
    {
        EmotionQuadrant.PleasantEnergetic => "#C26D53",   // Coral
        EmotionQuadrant.PleasantCalm => "#8FA083",        // Sage
        EmotionQuadrant.UnpleasantEnergetic => "#C9985A", // Amber
        EmotionQuadrant.UnpleasantCalm => "#929FA7",      // Ocean
        EmotionQuadrant.Neutral => "#6E6E6E",             // Gray500
        _ => "#D0D3D4"                                     // Dust (nothing recorded)
    };

    /// <summary>
    /// The resource key of the quadrant's highlight colour in the current theme. The brand themes map
    /// these to the same accents as <see cref="ToColorHex"/>; the pink theme maps them to pinks, so the
    /// mood row's highlights follow the theme while charts keep the fixed brand hues.
    /// </summary>
    public static string ToThemeKey(this EmotionQuadrant quadrant) => quadrant switch
    {
        EmotionQuadrant.PleasantEnergetic => "Theme_MoodPleasantEnergetic",
        EmotionQuadrant.PleasantCalm => "Theme_MoodPleasantCalm",
        EmotionQuadrant.UnpleasantEnergetic => "Theme_MoodUnpleasantEnergetic",
        EmotionQuadrant.UnpleasantCalm => "Theme_MoodUnpleasantCalm",
        EmotionQuadrant.Neutral => "Theme_MoodNeutral",
        _ => "Theme_Dust_Ocean"
    };

    /// <summary>Display name in the current UI language: the picker tile text and the donut legend.</summary>
    public static string ToLocalizedName(this EmotionQuadrant quadrant) => quadrant switch
    {
        EmotionQuadrant.PleasantEnergetic => AppResources.QuadrantPleasantEnergetic,
        EmotionQuadrant.PleasantCalm => AppResources.QuadrantPleasantCalm,
        EmotionQuadrant.UnpleasantEnergetic => AppResources.QuadrantUnpleasantEnergetic,
        EmotionQuadrant.UnpleasantCalm => AppResources.QuadrantUnpleasantCalm,
        EmotionQuadrant.Neutral => AppResources.EmotionNeutral,
        _ => AppResources.EmotionNone
    };

    /// <summary>
    /// The face drawn for the quadrant on the mood row: an image bundled with the app (Microsoft Fluent
    /// Emoji, 3D), so the row looks the same on every phone. Empty for <see cref="EmotionQuadrant.None"/>.
    /// </summary>
    public static string ToIconFile(this EmotionQuadrant quadrant) => quadrant switch
    {
        EmotionQuadrant.PleasantEnergetic => "mood_pleasant_energetic.png",
        EmotionQuadrant.PleasantCalm => "mood_pleasant_calm.png",
        EmotionQuadrant.UnpleasantEnergetic => "mood_unpleasant_energetic.png",
        EmotionQuadrant.UnpleasantCalm => "mood_unpleasant_calm.png",
        EmotionQuadrant.Neutral => "mood_neutral.png",
        _ => string.Empty
    };

    /// <summary>The glyph on the picker tile — the one the representative emotion has always worn.</summary>
    public static string ToEmoji(this EmotionQuadrant quadrant) => quadrant switch
    {
        EmotionQuadrant.PleasantEnergetic => "😊",
        EmotionQuadrant.PleasantCalm => "😌",
        EmotionQuadrant.UnpleasantEnergetic => "😰",
        EmotionQuadrant.UnpleasantCalm => "😢",
        EmotionQuadrant.Neutral => "😐",
        _ => string.Empty
    };

    /// <summary>What a bare tap on this quadrant records. See <see cref="EmotionCatalog.Representative"/>.</summary>
    public static Emotion Representative(this EmotionQuadrant quadrant) => EmotionCatalog.Representative(quadrant);

    /// <summary>The quadrant's emotions in picker order. See <see cref="EmotionCatalog.Emotions"/>.</summary>
    public static IReadOnlyList<Emotion> Emotions(this EmotionQuadrant quadrant) => EmotionCatalog.Emotions(quadrant);
}
