using System.Threading.Tasks;
using Diarion.Services.Review;

namespace Diarion.Services;

/// <summary>
/// Where there is no store to rate on yet — Android until the Play listing exists, Windows — the
/// request goes nowhere, and <see cref="ReviewPromptService"/> still records the date so the policy
/// behaves the same on every platform.
/// </summary>
public sealed class NullReviewPrompter : IReviewPrompter
{
    public Task<bool> RequestAsync() => Task.FromResult(false);
}
