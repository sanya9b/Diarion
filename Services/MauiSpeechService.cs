using System.Globalization;
using Microsoft.Maui.Media;

namespace Diarion.Services;

/// <summary>
/// The platform synthesizer behind <see cref="ISpeechService"/> (spec 19). Speaking goes through MAUI's
/// own <see cref="ITextToSpeech"/> on every platform; rendering to a notification sound exists only on
/// iOS, the one platform that will not run app code when a notification fires, and lives in
/// <c>Platforms/iOS/SpokenReminderSoundRenderer</c>. Android speaks at delivery instead, so for it the
/// render answers null on purpose.
/// </summary>
public sealed class MauiSpeechService : ISpeechService
{
    private readonly ITextToSpeech _tts;
    private Locale? _resolvedLocale;
    private string? _resolvedFor;

    /// <summary>Rendering that has not finished in this long is not going to; the reminder goes out silent.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(20);

    public MauiSpeechService() : this(TextToSpeech.Default) { }

    internal MauiSpeechService(ITextToSpeech tts) => _tts = tts;

    private static string CurrentLanguage()
        => (Diarion.Resources.Localization.AppResources.Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var locale = await ResolveLocaleAsync().ConfigureAwait(false);
            var options = locale is null ? null : new SpeechOptions { Locale = locale };
            await _tts.SpeakAsync(text, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A reminder that cannot be spoken was still shown; nothing here is worth a crash.
            System.Diagnostics.Debug.WriteLine($"Spoken reminder failed: {ex.Message}");
        }
    }

    public async Task<string?> RenderToNotificationSoundAsync(string text, string fileName, CancellationToken cancellationToken = default)
    {
#if IOS
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var locale = await ResolveLocaleAsync().ConfigureAwait(false);
            var render = SpokenReminderSoundRenderer.RenderAsync(text, fileName, locale?.Language);
            var finished = await Task.WhenAny(render, Task.Delay(RenderTimeout, cancellationToken)).ConfigureAwait(false);
            return finished == render ? await render.ConfigureAwait(false) : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rendering a spoken reminder failed: {ex.Message}");
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    public void DeleteNotificationSound(string fileName)
    {
#if IOS
        SpokenReminderSoundRenderer.Delete(fileName);
#endif
    }

    public async Task<bool> HasVoiceForCurrentLanguageAsync()
    {
        try
        {
            return await ResolveLocaleAsync().ConfigureAwait(false) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The first installed voice for the app's language, or null when there is none — in which case the
    /// synthesizer is left to its system default, which is still a reminder, just an accented one.
    /// Cached per language: the device's voice list does not change under a running app.
    /// </summary>
    private async Task<Locale?> ResolveLocaleAsync()
    {
        var language = CurrentLanguage();
        if (_resolvedFor == language) return _resolvedLocale;

        var locales = await _tts.GetLocalesAsync().ConfigureAwait(false);
        var match = locales.FirstOrDefault(l => MatchesLanguage(l, language));
        _resolvedLocale = match;
        _resolvedFor = language;
        return match;
    }

    internal static bool MatchesLanguage(Locale locale, string twoLetterLanguage)
    {
        var code = locale.Language ?? string.Empty;
        // Android answers "uk"; iOS answers "uk-UA"; Windows answers "uk-UA" too. All begin the same way.
        return code.Equals(twoLetterLanguage, StringComparison.OrdinalIgnoreCase)
            || code.StartsWith(twoLetterLanguage + "-", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith(twoLetterLanguage + "_", StringComparison.OrdinalIgnoreCase);
    }
}
