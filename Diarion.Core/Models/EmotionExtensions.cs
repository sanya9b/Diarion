using System.Collections.Generic;
using Diarion.Resources.Localization;

namespace Diarion.Models;

/// <summary>
/// Places each <see cref="Emotion"/> on the circumplex plane. Valence (-2 unpleasant .. +2 pleasant) is
/// what statistics, prompts and correlations have always averaged and trended; arousal (-2 calm .. +2
/// energetic) is the second axis the same model gives for free. <see cref="Emotion.None"/> means "not
/// recorded" and maps to 0 on both axes only so arithmetic never faults — <see cref="Diarion.Services.MoodAggregate"/>
/// drops it before averaging. <see cref="Emotion.Neutral"/> is a recorded (0, 0).
/// </summary>
public static class EmotionExtensions
{
    /// <summary>
    /// (Valence, Arousal) per emotion. The original five keep the valences they shipped with, so no
    /// stored trend moves. Signs agree with the quadrant each emotion is filed under in
    /// <see cref="EmotionCatalog"/>: pleasant is strictly positive valence, energetic strictly positive
    /// arousal, and the tests hold that line.
    /// </summary>
    private static readonly IReadOnlyDictionary<Emotion, (int Valence, int Arousal)> Plane =
        new Dictionary<Emotion, (int, int)>
        {
            // Pleasant, energetic
            [Emotion.Happy] = (2, 1),
            [Emotion.Inspired] = (2, 2),
            [Emotion.Excited] = (2, 2),
            [Emotion.Proud] = (2, 1),
            [Emotion.Energetic] = (1, 2),
            [Emotion.Playful] = (1, 1),
            [Emotion.Optimistic] = (1, 1),

            // Pleasant, calm
            [Emotion.Calm] = (1, -1),
            [Emotion.Content] = (1, -1),
            [Emotion.Grateful] = (2, -1),
            [Emotion.Relaxed] = (1, -2),
            [Emotion.Loving] = (2, -1),
            [Emotion.Secure] = (1, -1),
            [Emotion.Peaceful] = (1, -2),

            // Unpleasant, energetic
            [Emotion.Anxious] = (-1, 1),
            [Emotion.Angry] = (-2, 2),
            [Emotion.Stressed] = (-1, 2),
            [Emotion.Irritated] = (-1, 1),
            [Emotion.Overwhelmed] = (-2, 1),
            [Emotion.Scared] = (-2, 2),
            [Emotion.Frustrated] = (-1, 1),

            // Unpleasant, calm
            [Emotion.Sad] = (-2, -1),
            [Emotion.Tired] = (-1, -2),
            [Emotion.Bored] = (-1, -1),
            [Emotion.Lonely] = (-2, -1),
            [Emotion.Disappointed] = (-1, -1),
            [Emotion.Exhausted] = (-2, -2),
            [Emotion.Hopeless] = (-2, -2),

            [Emotion.Neutral] = (0, 0)
        };

    /// <summary>How pleasant the state is, -2..+2. 0 for <see cref="Emotion.None"/> and <see cref="Emotion.Neutral"/>.</summary>
    public static int ToValence(this Emotion emotion) =>
        Plane.TryGetValue(emotion, out var point) ? point.Valence : 0;

    /// <summary>How energetic the state is, -2..+2. 0 for <see cref="Emotion.None"/> and <see cref="Emotion.Neutral"/>.</summary>
    public static int ToArousal(this Emotion emotion) =>
        Plane.TryGetValue(emotion, out var point) ? point.Arousal : 0;

    /// <summary>The quadrant this emotion is filed under in <see cref="EmotionCatalog"/>.</summary>
    public static EmotionQuadrant ToQuadrant(this Emotion emotion) => EmotionCatalog.QuadrantOf(emotion);

    /// <summary>
    /// Brand colour (hex) shared by the donut, the Year-in-Pixels heatmap, the hour grid and the
    /// picker. Emotions take the hue of their quadrant: these are fixed brand hues, not theme-dependent.
    /// </summary>
    public static string ToColorHex(this Emotion emotion) => emotion.ToQuadrant().ToColorHex();

    /// <summary>Display name in the current UI language, for the picker, legends, chart labels and screen readers.</summary>
    public static string ToLocalizedName(this Emotion emotion) => emotion switch
    {
        Emotion.Happy => AppResources.EmotionHappy,
        Emotion.Calm => AppResources.EmotionCalm,
        Emotion.Anxious => AppResources.EmotionAnxious,
        Emotion.Sad => AppResources.EmotionSad,
        Emotion.Angry => AppResources.EmotionAngry,
        Emotion.Neutral => AppResources.EmotionNeutral,

        Emotion.Inspired => AppResources.EmotionInspired,
        Emotion.Excited => AppResources.EmotionExcited,
        Emotion.Proud => AppResources.EmotionProud,
        Emotion.Energetic => AppResources.EmotionEnergetic,
        Emotion.Playful => AppResources.EmotionPlayful,
        Emotion.Optimistic => AppResources.EmotionOptimistic,

        Emotion.Content => AppResources.EmotionContent,
        Emotion.Grateful => AppResources.EmotionGrateful,
        Emotion.Relaxed => AppResources.EmotionRelaxed,
        Emotion.Loving => AppResources.EmotionLoving,
        Emotion.Secure => AppResources.EmotionSecure,
        Emotion.Peaceful => AppResources.EmotionPeaceful,

        Emotion.Stressed => AppResources.EmotionStressed,
        Emotion.Irritated => AppResources.EmotionIrritated,
        Emotion.Overwhelmed => AppResources.EmotionOverwhelmed,
        Emotion.Scared => AppResources.EmotionScared,
        Emotion.Frustrated => AppResources.EmotionFrustrated,

        Emotion.Tired => AppResources.EmotionTired,
        Emotion.Bored => AppResources.EmotionBored,
        Emotion.Lonely => AppResources.EmotionLonely,
        Emotion.Disappointed => AppResources.EmotionDisappointed,
        Emotion.Exhausted => AppResources.EmotionExhausted,
        Emotion.Hopeless => AppResources.EmotionHopeless,

        _ => AppResources.EmotionNone
    };
}
