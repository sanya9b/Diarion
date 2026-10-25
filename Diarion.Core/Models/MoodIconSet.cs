namespace Diarion.Models;

/// <summary>
/// What the five faces of the mood row look like (spec 18). Only the picture changes: the quadrants,
/// the colours and what is recorded stay the same in every set. Stored on the device, like the theme.
/// </summary>
public enum MoodIconSet
{
    /// <summary>The phone's own emoji as text, as in version 1.0: Apple's on iPhone, Google's on Android.</summary>
    Emoji = 0,

    /// <summary>Drawn weather: rain, storm, cloud, a quiet moon, sun.</summary>
    Weather = 1,

    /// <summary>One drawn plant in a terracotta pot: wilted, in the wind, a sprout, a tulip, in bloom.</summary>
    Plants = 2
}
