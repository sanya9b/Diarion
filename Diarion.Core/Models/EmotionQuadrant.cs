namespace Diarion.Models;

/// <summary>
/// The four regions of the circumplex plane (Russell's model: valence across, arousal up), plus the
/// origin. This is the level the picker opens on, the level charts colour by, and the level that owns a
/// brand hue — individual emotions inherit the colour of their quadrant, which is what keeps a palette
/// of four accents sufficient for twenty-nine states.
/// </summary>
public enum EmotionQuadrant
{
    /// <summary>No emotion recorded.</summary>
    None = 0,

    /// <summary>Pleasant and energetic: joy, inspiration, pride. Coral.</summary>
    PleasantEnergetic,

    /// <summary>Pleasant and calm: calm, content, grateful. Sage.</summary>
    PleasantCalm,

    /// <summary>Unpleasant and energetic: anxiety, anger, stress. Amber.</summary>
    UnpleasantEnergetic,

    /// <summary>Unpleasant and calm: sadness, tiredness, loneliness. Ocean.</summary>
    UnpleasantCalm,

    /// <summary>
    /// The origin rather than a quadrant: a recorded "nothing in particular". Kept in this enum so
    /// every recorded emotion resolves to exactly one group for grouping and colouring.
    /// </summary>
    Neutral
}
