namespace Diarion.Models;

/// <summary>
/// A weekly quota: "any N days this week", with no fixed days. Deliberately not a
/// <see cref="RecurrenceRule"/> — it answers "how many times", not "which days", and strength and streak
/// for it are counted in whole weeks rather than by walking days. The two used to share one enum, which
/// forced <c>IsScheduledOn</c> to answer <c>true</c> for every day and left the callers to fork around
/// that lie.
/// </summary>
public class CompletionTarget
{
    /// <summary>Weekly target (1–7).</summary>
    public int TimesPerWeek { get; set; } = 3;
}
