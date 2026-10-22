using AVFoundation;
using Foundation;

namespace Diarion.Services;

/// <summary>
/// Renders a phrase to a sound file in the app's <c>Library/Sounds</c>, which is where
/// <c>UNNotificationSound</c> looks for a sound named on a notification (spec 19). That is how a
/// reminder is spoken on iOS with the app not running: the system will not run app code when a
/// notification fires, but it will play the sound the notification names, so the speech is made ahead
/// of time, at scheduling, with the device's own synthesizer. Nothing leaves the phone.
/// <para>
/// The file is Core Audio Format, linear PCM, straight from the synthesizer's buffers. iOS plays a
/// notification sound of up to thirty seconds and falls back to the default chime past that, which is
/// why <see cref="ReminderPhraseBuilder.MaxTaskLength"/> is what it is.
/// </para>
/// </summary>
internal static class SpokenReminderSoundRenderer
{
    private static readonly string SoundsDirectory = Path.Combine(
        NSSearchPath.GetDirectories(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User, true)[0],
        "Sounds");

    // Synthesizers rendering right now. The callback arrives on a background queue after this method
    // has returned, and a collected synthesizer stops calling back without a word.
    private static readonly HashSet<AVSpeechSynthesizer> InFlight = new();

    public static Task<string?> RenderAsync(string text, string fileName, string? language)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(SoundsDirectory, fileName);

        try
        {
            Directory.CreateDirectory(SoundsDirectory);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spoken reminder: cannot prepare {path}: {ex.Message}");
            return Task.FromResult<string?>(null);
        }

        var utterance = new AVSpeechUtterance(text);
        if (language is not null)
        {
            // Null when the device has no voice for the language; the utterance then takes the system
            // default, which is the same fallback SpeakAsync has.
            utterance.Voice = AVSpeechSynthesisVoice.FromLanguage(language);
        }

        var synthesizer = new AVSpeechSynthesizer();
        lock (InFlight) InFlight.Add(synthesizer);

        AVAudioFile? file = null;

        void Finish(string? result)
        {
            file?.Dispose();
            file = null;
            lock (InFlight) InFlight.Remove(synthesizer);
            if (result is null)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
            }
            completion.TrySetResult(result);
        }

        // On the main thread: the synthesizer is a UIKit-era object and is happiest started there; its
        // buffers still arrive on a queue of its own.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
        try
        {
            synthesizer.WriteUtterance(utterance, buffer =>
            {
                if (completion.Task.IsCompleted) return;
                try
                {
                    // The synthesizer signals the end of the utterance with an empty buffer.
                    if (buffer is not AVAudioPcmBuffer pcm || pcm.FrameLength == 0)
                    {
                        Finish(File.Exists(path) ? fileName : null);
                        return;
                    }

                    if (file is null)
                    {
                        file = new AVAudioFile(NSUrl.FromFilename(path), pcm.Format.Settings, out var openError);
                        if (openError is not null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Spoken reminder: cannot open {path}: {openError.LocalizedDescription}");
                            Finish(null);
                            return;
                        }
                    }

                    file.WriteFromBuffer(pcm, out var writeError);
                    if (writeError is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Spoken reminder: write failed: {writeError.LocalizedDescription}");
                        Finish(null);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Spoken reminder: render failed: {ex.Message}");
                    Finish(null);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spoken reminder: synthesizer refused: {ex.Message}");
            Finish(null);
        }
        });

        return completion.Task;
    }

    public static void Delete(string fileName)
    {
        try
        {
            var path = Path.Combine(SoundsDirectory, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spoken reminder: cannot delete {fileName}: {ex.Message}");
        }
    }
}
