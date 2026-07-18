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

    /// <summary>
    /// Brand color (hex) for each emotion, shared by the emotion donut, the Year-in-Pixels heatmap and
    /// anywhere else emotions are color-coded. These are fixed brand hues, not theme-dependent.
    /// </summary>
    public static string ToColorHex(this Emotion emotion) => emotion switch
    {
        Emotion.Happy => "#C26D53",   // Coral
        Emotion.Calm => "#8FA083",    // Sage
        Emotion.Anxious => "#C9985A", // Amber
        Emotion.Sad => "#929FA7",     // Ocean
        Emotion.Angry => "#A87C8E",   // Berry
        _ => "#D0D3D4"                 // Dust (neutral / none)
    };
}
