using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Review;

/// <summary>Call after a diary entry was saved; asks for a rating when <see cref="ReviewPromptPolicy"/> allows.</summary>
public interface IReviewPromptService
{
    /// <summary>Never throws and never blocks the save: a failed or declined prompt is simply no prompt.</summary>
    Task MaybeRequestAsync();
}

/// <summary>
/// Decides from the diary itself — days with an entry, read through <see cref="IDiaryService"/> — and
/// remembers the last request in <see cref="Models.UserProfile.ReviewPromptedAt"/>, so the quiet period
/// survives reinstalls that restore a backup and is not tied to this device's preferences.
/// <para>
/// The date is written <em>before</em> the OS is asked, not after: iOS gives no answer about whether the
/// sheet appeared, and asking again next save because "nothing happened" is exactly the nagging the
/// policy exists to prevent.
/// </para>
/// </summary>
public sealed class ReviewPromptService : IReviewPromptService
{
    /// <summary>How far back to count days with entries; anything older is not what we are thanking them for.</summary>
    private const int LookbackDays = 365;

    private readonly IDiaryService _diary;
    private readonly IProfileService _profiles;
    private readonly IReviewPrompter _prompter;
    private readonly Func<DateTime> _today;
    private int _inFlight;

    public ReviewPromptService(IDiaryService diary, IProfileService profiles, IReviewPrompter prompter, Func<DateTime>? today = null)
    {
        _diary = diary;
        _profiles = profiles;
        _prompter = prompter;
        _today = today ?? (() => DateTime.Today);
    }

    public async Task MaybeRequestAsync()
    {
        // Autosave and an explicit save can land within the same second; one check at a time is enough.
        if (Interlocked.Exchange(ref _inFlight, 1) == 1) return;
        try
        {
            var today = _today().Date;
            var profile = await _profiles.GetUserProfileAsync();
            if (!ReviewPromptPolicy.CooldownAllows(profile.ReviewPromptedAt, today)) return;

            var days = await _diary.GetContentDatesAsync(today.AddDays(-LookbackDays), today);
            if (!ReviewPromptPolicy.ShouldPrompt(days.Count, profile.ReviewPromptedAt, today)) return;

            profile.ReviewPromptedAt = today;
            await _profiles.SaveUserProfileAsync(profile);
            await _prompter.RequestAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Review prompt skipped: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }
}
