namespace Diarion.Models;

/// <summary>
/// The kind of reflection a guided prompt invites. Chosen from the day's mood: a hard day gets a
/// grounding CBT question rather than an invitation to celebrate.
/// </summary>
public enum PromptCategory
{
    /// <summary>Neutral or unknown mood — an open question about the day.</summary>
    OpenReflection = 0,

    /// <summary>Low mood — examine the thought behind it rather than argue with the feeling.</summary>
    CbtReframe = 1,

    /// <summary>Good mood — dwell on what went well instead of letting it slide past.</summary>
    Savouring = 2,

    /// <summary>Good mood, gratitude not yet written — the evening gratitude ritual.</summary>
    EveningGratitude = 3
}
