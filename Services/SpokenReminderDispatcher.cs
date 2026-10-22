#if ANDROID || IOS || MACCATALYST
using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;
#endif

namespace Diarion.Services;

/// <summary>
/// Reads a spoken reminder aloud the moment its notification arrives (spec 19). On Android the plugin
/// raises the arrival from its alarm receiver, inside the app's process, whether or not the app is on
/// screen — so this is how Android speaks with the app in the background, with no extra permission and
/// no foreground service. On iOS the event only fires with the app in front, and a reminder that
/// carries its own pre-rendered sound is already being played by the system, so it is left alone;
/// only one whose rendering failed is spoken here, as a fallback.
/// <para>
/// Started from <c>MauiProgram</c> rather than from the window, because on Android the process that
/// receives the alarm has an application and no window.
/// </para>
/// </summary>
public sealed class SpokenReminderDispatcher
{
    private readonly ISpeechService _speech;
    private bool _started;

    public SpokenReminderDispatcher(ISpeechService speech) => _speech = speech;

    public void Start()
    {
        if (_started) return;
        _started = true;
#if ANDROID || IOS || MACCATALYST
        LocalNotificationCenter.Current.NotificationReceived += OnNotificationReceived;
#endif
    }

#if ANDROID || IOS || MACCATALYST
    private void OnNotificationReceived(NotificationEventArgs e)
    {
        var request = e.Request;
        if (request is null) return;
        var platformPlaysNamedSound = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();
        if (!SpokenReminderPayload.ShouldSpeak(request.ReturningData, request.Sound, platformPlaysNamedSound, out var phrase)) return;

        // Fire and forget: the receiver must return, and the utterance outlives it by a few seconds.
        _ = _speech.SpeakAsync(phrase);
    }
#endif
}
