using System.Threading.Tasks;

namespace Diarion.Services.Review;

/// <summary>
/// The platform half of the rating request: shows the system "Enjoying Diarion?" sheet, or does
/// nothing where there is no store to rate on. The decision <em>whether</em> to ask lives in
/// <see cref="ReviewPromptService"/>; this only knows <em>how</em>.
/// </summary>
public interface IReviewPrompter
{
    /// <summary>
    /// Asks the OS to show its rating prompt. Returns true when the request was handed to the OS —
    /// which is not the same as the sheet being shown: iOS decides that on its own and may stay silent.
    /// </summary>
    Task<bool> RequestAsync();
}
