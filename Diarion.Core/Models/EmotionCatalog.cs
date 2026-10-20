using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Models;

/// <summary>
/// Which emotions belong to which quadrant, and in what order a picker should offer them. The single
/// source of membership: <see cref="EmotionExtensions.ToQuadrant"/> reads the reverse of this table, so
/// an emotion cannot be filed under one quadrant here and coloured as another elsewhere. The first entry
/// of each list is the quadrant's representative — the original five, and <see cref="Emotion.Neutral"/>.
/// </summary>
public static class EmotionCatalog
{
    private static readonly IReadOnlyDictionary<EmotionQuadrant, IReadOnlyList<Emotion>> Members =
        new Dictionary<EmotionQuadrant, IReadOnlyList<Emotion>>
        {
            [EmotionQuadrant.PleasantEnergetic] = new[]
            {
                Emotion.Happy, Emotion.Inspired, Emotion.Excited, Emotion.Proud,
                Emotion.Energetic, Emotion.Playful, Emotion.Optimistic
            },
            [EmotionQuadrant.PleasantCalm] = new[]
            {
                Emotion.Calm, Emotion.Content, Emotion.Grateful, Emotion.Relaxed,
                Emotion.Loving, Emotion.Secure, Emotion.Peaceful
            },
            [EmotionQuadrant.UnpleasantEnergetic] = new[]
            {
                Emotion.Anxious, Emotion.Angry, Emotion.Stressed, Emotion.Irritated,
                Emotion.Overwhelmed, Emotion.Scared, Emotion.Frustrated
            },
            [EmotionQuadrant.UnpleasantCalm] = new[]
            {
                Emotion.Sad, Emotion.Tired, Emotion.Bored, Emotion.Lonely,
                Emotion.Disappointed, Emotion.Exhausted, Emotion.Hopeless
            },
            [EmotionQuadrant.Neutral] = new[] { Emotion.Neutral }
        };

    private static readonly IReadOnlyDictionary<Emotion, EmotionQuadrant> QuadrantByEmotion =
        Members.SelectMany(kv => kv.Value.Select(e => (Emotion: e, Quadrant: kv.Key)))
            .ToDictionary(x => x.Emotion, x => x.Quadrant);

    /// <summary>The recordable quadrants in model order; <see cref="EmotionQuadrant.None"/> excluded.</summary>
    public static IReadOnlyList<EmotionQuadrant> Quadrants { get; } = new[]
    {
        EmotionQuadrant.PleasantEnergetic,
        EmotionQuadrant.PleasantCalm,
        EmotionQuadrant.UnpleasantEnergetic,
        EmotionQuadrant.UnpleasantCalm,
        EmotionQuadrant.Neutral
    };

    /// <summary>Every recordable emotion, quadrant by quadrant, representative first.</summary>
    public static IReadOnlyList<Emotion> All { get; } = Quadrants.SelectMany(q => Members[q]).ToList();

    /// <summary>The quadrant's emotions in picker order, representative first. Empty for <see cref="EmotionQuadrant.None"/>.</summary>
    public static IReadOnlyList<Emotion> Emotions(EmotionQuadrant quadrant) =>
        Members.TryGetValue(quadrant, out var list) ? list : Array.Empty<Emotion>();

    /// <summary>What a bare quadrant tap records: the first of its emotions.</summary>
    public static Emotion Representative(EmotionQuadrant quadrant)
    {
        var list = Emotions(quadrant);
        return list.Count > 0 ? list[0] : Emotion.None;
    }

    /// <summary>The quadrant an emotion is filed under; <see cref="EmotionQuadrant.None"/> for <see cref="Emotion.None"/>.</summary>
    public static EmotionQuadrant QuadrantOf(Emotion emotion) =>
        QuadrantByEmotion.TryGetValue(emotion, out var quadrant) ? quadrant : EmotionQuadrant.None;
}
