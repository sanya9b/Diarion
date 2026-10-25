using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>
/// The device's own speech synthesizer, as much of it as a spoken reminder needs (spec 19). Everything
/// here is on-device: the text never leaves the phone, which is the one property that lets a diary
/// read its tasks out loud at all.
/// </summary>
public interface ISpeechService
{
    /// <summary>
    /// Says the text now, in the app's language where the device has a voice for it and in the system
    /// voice where it does not. Completes when the utterance ends. Never throws for a missing voice or
    /// engine — a reminder that cannot be spoken is still a reminder that was shown.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the text to a sound file the operating system can attach to a notification, so it plays
    /// with the app not running. Returns the file name to put on the notification, or null where the
    /// platform does not need one (it speaks at delivery instead) or could not render. Overwrites a file
    /// of the same name.
    /// </summary>
    Task<string?> RenderToNotificationSoundAsync(string text, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Removes a file made by <see cref="RenderToNotificationSoundAsync"/>; a missing file is not an error.</summary>
    void DeleteNotificationSound(string fileName);

    /// <summary>Whether the device has a voice for the app's current language.</summary>
    Task<bool> HasVoiceForCurrentLanguageAsync();
}
