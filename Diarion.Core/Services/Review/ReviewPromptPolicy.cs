using System;

namespace Diarion.Services.Review;

/// <summary>
/// When it is fair to ask for an App Store rating. Pure: two numbers and two dates in, a yes/no out,
/// so the boundaries are pinned by tests rather than by what the store happens to show.
/// <para>
/// Not on the first entry — the person does not know yet whether they like the app — but after a
/// week of days with something written, which is the point where a diary has become a habit. And
/// never more than once a season: iOS caps the sheet at three a year anyway, and a prompt that keeps
/// coming back is the quickest way to a one-star review.
/// </para>
/// </summary>
public static class ReviewPromptPolicy
{
    /// <summary>Days with an entry before the first request.</summary>
    public const int MinDaysWithEntries = 7;

    /// <summary>Quiet period after a request, whether or not iOS actually showed the sheet.</summary>
    public const int CooldownDays = 120;

    public static bool CooldownAllows(DateTime? lastPromptedAt, DateTime today)
        => lastPromptedAt is null || (today.Date - lastPromptedAt.Value.Date).TotalDays >= CooldownDays;

    public static bool ShouldPrompt(int daysWithEntries, DateTime? lastPromptedAt, DateTime today)
        => daysWithEntries >= MinDaysWithEntries && CooldownAllows(lastPromptedAt, today);
}
