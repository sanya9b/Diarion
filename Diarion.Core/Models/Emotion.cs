namespace Diarion.Models;

/// <summary>
/// Everything a day or an hour can be logged as. Each member sits on the circumplex plane — see
/// <see cref="EmotionExtensions.ToValence"/> and <see cref="EmotionExtensions.ToArousal"/> — and belongs
/// to exactly one <see cref="EmotionQuadrant"/> through <see cref="EmotionCatalog"/>.
/// <para>
/// Append-only. LiteDB stores these by name and exports print them, so existing members are never
/// renamed, removed or reordered: <see cref="Dominant"/> tie-breaks on enum order and a renamed member
/// would turn every logged day into <see cref="None"/>. The first five are the original set and double
/// as the representative of their quadrant — the value a quadrant tap records before any refinement.
/// </para>
/// </summary>
public enum Emotion
{
    /// <summary>Nothing recorded. Not a feeling, and never counted as one.</summary>
    None = 0,

    Happy,
    Calm,
    Anxious,
    Sad,
    Angry,

    /// <summary>The origin of the plane: logged deliberately, unlike <see cref="None"/>.</summary>
    Neutral,

    // Pleasant, energetic
    Inspired,
    Excited,
    Proud,
    Energetic,
    Playful,
    Optimistic,

    // Pleasant, calm
    Content,
    Grateful,
    Relaxed,
    Loving,
    Secure,
    Peaceful,

    // Unpleasant, energetic
    Stressed,
    Irritated,
    Overwhelmed,
    Scared,
    Frustrated,

    // Unpleasant, calm
    Tired,
    Bored,
    Lonely,
    Disappointed,
    Exhausted,
    Hopeless
}
