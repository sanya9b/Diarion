namespace Diarion.Models;

/// <summary>
/// Maps the qualitative <see cref="Emotion"/> to a numeric valence score (-2..+2) so mood can be
/// averaged, trended, and correlated with other factors (used by statistics and the future
/// correlation engine). Neutral/None maps to 0.
/// </summary>
public static class EmotionExtensions
{
    public static int ToValence(this Emotion emotion) => emotion switch
    {
        Emotion.Happy => 2,
        Emotion.Calm => 1,
        Emotion.Anxious => -1,
        Emotion.Sad => -2,
        Emotion.Angry => -2,
        _ => 0
    };
}
