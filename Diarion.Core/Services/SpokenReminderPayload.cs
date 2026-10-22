namespace Diarion.Services;

/// <summary>
/// What a spoken reminder carries inside the notification so the app knows, when the notification
/// arrives, that this one is to be read aloud and what to say (spec 19). The notification's payload is
/// the only thing that survives the app being gone between scheduling and delivery, which is why the
/// phrase travels in it rather than being looked up again.
/// </summary>
public static class SpokenReminderPayload
{
    private const string Prefix = "spoken:";

    public static string Encode(string phrase) => Prefix + phrase;

    /// <summary>True when the payload marks a spoken reminder; <paramref name="phrase"/> is then what to say.</summary>
    public static bool TryDecode(string? payload, out string phrase)
    {
        if (payload != null && payload.StartsWith(Prefix, System.StringComparison.Ordinal) && payload.Length > Prefix.Length)
        {
            phrase = payload[Prefix.Length..];
            return true;
        }

        phrase = string.Empty;
        return false;
    }

    /// <summary>
    /// Whether the app itself should say the reminder when its notification arrives. Apple plays a sound
    /// named on the notification on its own, so there the app only speaks when no sound was rendered;
    /// everywhere else the app is the only mouth there is.
    /// </summary>
    public static bool ShouldSpeak(string? payload, string? sound, bool platformPlaysNamedSound, out string phrase)
    {
        if (!TryDecode(payload, out phrase)) return false;
        if (platformPlaysNamedSound && !string.IsNullOrEmpty(sound)) return false;
        return true;
    }
}
